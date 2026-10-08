using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using FileService.Contracts.Dtos;
using FileService.Contracts.HttpCommunication;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record UpdateMaterialCommand(Guid MaterialId, UpdateMaterialRequest Request) : ICommand;

public sealed class UpdateMaterialRequestValidator : AbstractValidator<UpdateMaterialRequest>
{
    public UpdateMaterialRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);

        RuleFor(x => x.Content!)
            .MustBeValueObject(MarkdownContent.Create)
            .When(x => !string.IsNullOrWhiteSpace(x.Content));

        RuleFor(x => x.Description!)
            .MustBeValueObject(MarkdownContent.Create)
            .When(x => !string.IsNullOrWhiteSpace(x.Description));

        RuleFor(x => x.Kind)
            .Must(value => Enum.TryParse<MaterialKind>(value, ignoreCase: true, out _))
            .WithError(GeneralErrors.ValueIsInvalid(nameof(UpdateMaterialRequest.Kind)));

        RuleFor(x => x.AccessType)
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType());
    }
}

public sealed class UpdateMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("materials/{materialId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid materialId,
                    [FromBody] UpdateMaterialRequest request,
                    [FromServices] UpdateMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateMaterialCommand(materialId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class UpdateMaterialHandler : ICommandHandler<Guid, UpdateMaterialCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IValidator<UpdateMaterialRequest> _validator;
    private readonly HybridCache _cache;
    private readonly ILogger<UpdateMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateMaterialHandler(
        IMaterialsRepository materialsRepository,
        IQuizzesRepository quizzesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IFileServiceClient fileServiceClient,
        IValidator<UpdateMaterialRequest> validator,
        HybridCache cache,
        ILogger<UpdateMaterialHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _quizzesRepository = quizzesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _fileServiceClient = fileServiceClient;
        _validator = validator;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        UpdateMaterialCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId,
            cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        Material material = materialResult.Value;

        UnitResult<Error> ownership = await _userScopedData.CheckMaterialOwnershipAsync(
            material.Id, material.AuthorId, _materialsRepository, cancellationToken);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;

        MarkdownContent? content = !string.IsNullOrWhiteSpace(command.Request.Content)
            ? MarkdownContent.Create(command.Request.Content).Value
            : null;

        MarkdownContent? description = !string.IsNullOrWhiteSpace(command.Request.Description)
            ? MarkdownContent.Create(command.Request.Description).Value
            : null;

        var kind = Enum.Parse<MaterialKind>(command.Request.Kind, ignoreCase: true);
        var accessType = Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        UnitResult<Error> payloadResult = material.ValidateProspectivePayload(content, command.Request.VideoId);
        if (payloadResult.IsFailure)
            return payloadResult.Error;

        bool titleConflict = await _materialsRepository.ExistsByTitleAsync(
            title, material.Id, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Material", title.Value);

        List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(material.Id, cancellationToken);
        AccessType previousAccessType = material.AccessType;

        // Capture media ids BEFORE material.Update() to avoid coupling the diff
        // to whatever the aggregate happens to do internally. Today Update() does
        // not touch VideoId/ImageId, but this keeps the contract robust.
        Guid? previousVideoId = material.VideoId?.Value;
        Guid? previousImageId = material.ImageId?.Value;
        long previousVideoBindingRevision = material.VideoBindingRevision;
        long previousImageBindingRevision = material.ImageBindingRevision;

        UnitResult<Error> updateResult = material.Update(
            title,
            content,
            kind,
            accessType,
            courseIds.Count,
            description);
        if (updateResult.IsFailure)
            return updateResult.Error;

        // Sync quiz reference (#489): null ⇒ отвязать (квиз живёт дальше — он standalone),
        // значение ⇒ привязать. Квиз должен существовать и принадлежать caller'у
        // (admin/moderator — bypass). Делается ДО media-sync, чтобы валидационный отказ
        // не требовал компенсации FileService-биндов.
        if (command.Request.QuizId != material.QuizId)
        {
            if (command.Request.QuizId is { } requestedQuizId)
            {
                Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
                    q => q.Id == requestedQuizId, cancellationToken);
                if (quizResult.IsFailure)
                    return EducationErrors.QuizNotFound(requestedQuizId);

                UnitResult<Error> quizOwnership = _userScopedData.CheckOwnership(quizResult.Value.AuthorId);
                if (quizOwnership.IsFailure)
                    return quizOwnership.Error;

                material.AttachQuiz(requestedQuizId);
            }
            else
            {
                material.DetachQuiz();
            }
        }

        // Sync media binding diff. The new binding revision is committed with the
        // aggregate; only then can the durable detach delete the exact old revision.
        TargetEntityDto target = new("material", material.Id);
        UnitResult<Error> videoSync = await MediaAssetSync.SyncSingleAssetAsync(
            _fileServiceClient,
            _outbox,
            previous: previousVideoId,
            previousBindingRevision: previousVideoBindingRevision,
            requested: command.Request.VideoId,
            target: target,
            actorUserId: _userScopedData.UserId,
            actorCanManageAnyAsset: _userScopedData.IsAdmin,
            attach: (id, revision) => material.AttachVideo(VideoId.Create(id).Value, revision),
            detach: material.DetachVideo,
            cancellationToken);
        if (videoSync.IsFailure)
            return videoSync.Error;

        UnitResult<Error> previewSync = await MediaAssetSync.SyncSingleAssetAsync(
            _fileServiceClient,
            _outbox,
            previous: previousImageId,
            previousBindingRevision: previousImageBindingRevision,
            requested: command.Request.PreviewId,
            target: target,
            actorUserId: _userScopedData.UserId,
            actorCanManageAnyAsset: _userScopedData.IsAdmin,
            attach: (id, revision) => material.AttachImage(ImageId.Create(id).Value, revision),
            detach: material.DetachImage,
            cancellationToken);
        if (previewSync.IsFailure)
            return previewSync.Error;

        await _outbox.PublishAsync(new MaterialUpdated(material.Id));

        if (previousAccessType != accessType)
        {
            await _outbox.PublishAsync(new MaterialAccessChanged(
                material.Id,
                accessType.ToString(),
                courseIds,
                material.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Title/AccessType материала живут в curriculum-кэше курсов — сбрасываем
        // их, чтобы изменение тут же отражалось в сайдбаре, а не висело 3 минуты.
        // Недоступность Redis не откатывает уже зафиксированную транзакцию.
        try
        {
            foreach (Guid courseId in courseIds)
            {
                await CourseCacheInvalidator.InvalidateAsync(_cache, courseId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Curriculum cache invalidation failed after updating material {MaterialId} (will expire via TTL)",
                material.Id);
        }

        _logger.LogInformation("Material {MaterialId} updated", material.Id);

        return material.Id;
    }
}
