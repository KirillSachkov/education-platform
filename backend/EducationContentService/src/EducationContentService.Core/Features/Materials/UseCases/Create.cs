using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using FileService.Contracts.Assets;
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
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record CreateMaterialCommand(CreateMaterialRequest Request) : ICommand;

public sealed class CreateMaterialRequestValidator : AbstractValidator<CreateMaterialRequest>
{
    public CreateMaterialRequestValidator()
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
            .WithError(GeneralErrors.ValueIsInvalid(nameof(CreateMaterialRequest.Kind)));

        RuleFor(x => x.AccessType)
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType());
    }
}

public sealed class CreateMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("materials", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateMaterialRequest request,
                    [FromServices] CreateMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new CreateMaterialCommand(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Создаёт материал и, опционально, привязывает его к курсу и/или модулю в одной транзакции.
///     См. MATERIAL_LIFECYCLE.md сценарий 8 (создание из контекста модуля) и сценарий 1 (space-level).
///     <para>
///     Если в payload переданы <c>VideoId</c>/<c>PreviewId</c>, выполняется sync-bind ассета через
///     FileService (idempotent). Markdown-ассеты из контента всё ещё привязываются асинхронно
///     через <c>BindMaterialDraftAssets</c> event.
///     </para>
/// </summary>
public sealed class CreateMaterialHandler : ICommandHandler<Guid, CreateMaterialCommand>
{
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly CourseMaterialService _courseMaterialService;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly HybridCache _cache;
    private readonly IValidator<CreateMaterialRequest> _validator;
    private readonly ILogger<CreateMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public CreateMaterialHandler(
        IMaterialsRepository materialsRepository,
        ICoursesRepository coursesRepository,
        IModulesRepository modulesRepository,
        IQuizzesRepository quizzesRepository,
        CourseMaterialService courseMaterialService,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IFileServiceClient fileServiceClient,
        HybridCache cache,
        IValidator<CreateMaterialRequest> validator,
        ILogger<CreateMaterialHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _coursesRepository = coursesRepository;
        _modulesRepository = modulesRepository;
        _quizzesRepository = quizzesRepository;
        _courseMaterialService = courseMaterialService;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _fileServiceClient = fileServiceClient;
        _cache = cache;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateMaterialCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Pre-check publish invariant before any side effects: материал должен иметь
        // Content или VideoId на момент Publish() (см. Material.Publish). Дешевле
        // отвалиться здесь, чем делать sync-bind ассета и компенсировать его обратно.
        if (command.Request.PublishOnCreate
            && string.IsNullOrWhiteSpace(command.Request.Content)
            && command.Request.VideoId is null)
        {
            return EducationErrors.CannotPublishMaterialWithoutContent();
        }

        Title title = Title.Create(command.Request.Title).Value;

        MarkdownContent? content = !string.IsNullOrWhiteSpace(command.Request.Content)
            ? MarkdownContent.Create(command.Request.Content).Value
            : null;

        MarkdownContent? description = !string.IsNullOrWhiteSpace(command.Request.Description)
            ? MarkdownContent.Create(command.Request.Description).Value
            : null;

        var kind = Enum.Parse<MaterialKind>(command.Request.Kind, ignoreCase: true);
        var accessType = Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        // Resolve courseId: explicitly passed, or derived from module.
        Guid? courseId = command.Request.CourseId;
        if (command.Request.ModuleId is { } moduleId)
        {
            Guid? moduleCourseId = await _modulesRepository.GetCourseIdAsync(moduleId, cancellationToken);
            if (moduleCourseId is null)
                return EducationErrors.ModuleNotAttachedToCourse(moduleId);

            if (courseId is null)
                courseId = moduleCourseId;
            else if (courseId != moduleCourseId)
                return EducationErrors.ModuleCourseMismatch(moduleId, courseId.Value);
        }

        // Validate AccessType against future bound-course count (INV-2/INV-3).
        int boundCourseCount = courseId is null ? 0 : 1;
        UnitResult<Error> policyCheck = MaterialAccessPolicy.CanSetAccessType(accessType, boundCourseCount);
        if (policyCheck.IsFailure)
            return policyCheck.Error;

        // Course ownership check — author can only attach to their own courses.
        if (courseId is { } cid)
        {
            var courseResult = await _coursesRepository.GetByAsync(c => c.Id == cid, cancellationToken);
            if (courseResult.IsFailure)
                return courseResult.Error;

            UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
            if (ownership.IsFailure)
                return ownership.Error;
        }

        bool titleConflict = await _materialsRepository.ExistsByTitleAsync(
            title, excludeId: null, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Material", title.Value);

        // Admin-only override: служебные client_credentials caller'ы (MCP seed-сценарий)
        // явно передают AuthorId, потому что у service-токена sub=client_id → UserId=Guid.Empty.
        // Для не-admin caller'а поле игнорируется — author всегда берётся из request scope.
        Guid materialAuthorId =
            _userScopedData.IsAdmin && command.Request.AuthorId is { } overrideAuthorId
                ? overrideAuthorId
                : _userScopedData.UserId;

        var material = new Material(materialAuthorId, title, kind, accessType);

        // Optional quiz reference (#489): квиз должен существовать и принадлежать
        // caller'у (admin/moderator — bypass). Один квиз может переиспользоваться
        // несколькими материалами — duplicate-чека нет.
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

        if (content is not null)
        {
            // Политика уже проверена выше для accessType+boundCourseCount, повторный
            // прогон Update'а не нужен — используем целевой SetContent.
            material.SetContent(content);
        }

        if (description is not null)
        {
            material.SetDescription(description);
        }

        // Pre-validate the asset id value objects BEFORE doing any side effects.
        // Empty Guid would fail VideoId/ImageId.Create — surface that early so we
        // never call BindAssetAsync only to bail.
        VideoId? videoIdValue = null;
        if (command.Request.VideoId is { } vid)
        {
            Result<VideoId, Error> r = VideoId.Create(vid);
            if (r.IsFailure)
                return r.Error;
            videoIdValue = r.Value;
        }

        ImageId? imageIdValue = null;
        if (command.Request.PreviewId is { } pid)
        {
            Result<ImageId, Error> r = ImageId.Create(pid);
            if (r.IsFailure)
                return r.Error;
            imageIdValue = r.Value;
        }

        await _materialsRepository.AddAsync(material, cancellationToken);

        // Optional: attach to course (course_materials).
        if (courseId is { } attachCourseId)
        {
            Result<Domain.Courses.CourseMaterial, Error> cmResult = await _courseMaterialService.CreateAsync(
                attachCourseId, material.Id, cancellationToken);
            if (cmResult.IsFailure)
                return cmResult.Error;
        }

        // Optional: attach to module (module_items). Новые материалы по умолчанию
        // создаются с ViewPriority=Key — основной материал модуля (можно переключить
        // на Recommended/Supplementary через UpdateModuleItemViewPriority).
        if (command.Request.ModuleId is { } attachModuleId)
        {
            Result<ModuleItem, Error> miResult = await _moduleItemService.CreateAsync(
                attachModuleId, ModuleItemType.Material, material.Id, cancellationToken,
                ViewPriority.Key);
            if (miResult.IsFailure)
                return miResult.Error;
        }

        // Sync bind media assets — done last (right before save) so any earlier
        // validation failure short-circuits without preparing a binding. A prepared
        // revision is intentionally left unconfirmed on rollback: deleting it here
        // could race a parallel successful transaction selecting the same asset.
        TargetEntityDto target = new("material", material.Id);

        if (videoIdValue is { } videoVo)
        {
            Result<BindAssetResponse, Error> bindVideoResult = await _fileServiceClient.BindAssetInternalAsync(
                videoVo.Value,
                new BindAssetInternalRequest(
                    target,
                    _userScopedData.UserId,
                    _userScopedData.IsAdmin,
                    Guid.CreateVersion7()),
                cancellationToken);
            if (bindVideoResult.IsFailure)
                return bindVideoResult.Error;
            material.AttachVideo(videoVo, bindVideoResult.Value.BindingRevision);
            await _outbox.PublishAsync(new FileAssetBindingConfirmed(
                videoVo.Value,
                bindVideoResult.Value.BindingRevision));
        }

        if (imageIdValue is { } imageVo)
        {
            Result<BindAssetResponse, Error> bindPreviewResult = await _fileServiceClient.BindAssetInternalAsync(
                imageVo.Value,
                new BindAssetInternalRequest(
                    target,
                    _userScopedData.UserId,
                    _userScopedData.IsAdmin,
                    Guid.CreateVersion7()),
                cancellationToken);
            if (bindPreviewResult.IsFailure)
                return bindPreviewResult.Error;
            material.AttachImage(imageVo, bindPreviewResult.Value.BindingRevision);
            await _outbox.PublishAsync(new FileAssetBindingConfirmed(
                imageVo.Value,
                bindPreviewResult.Value.BindingRevision));
        }

        await _outbox.PublishAsync(new MaterialCreated(
            material.Id, material.AccessType.ToString(), material.AuthorId));

        // Publish access_changed so Redis gets the initial tags (including courseId if attached).
        if (courseId is { } accessCourseId)
        {
            await _outbox.PublishAsync(new MaterialAccessChanged(
                material.Id,
                material.AccessType.ToString(),
                new List<Guid> { accessCourseId },
                material.AuthorId));
        }

        if (!string.IsNullOrWhiteSpace(command.Request.DraftId))
        {
            await _outbox.PublishAsync(new BindMaterialDraftAssets(
                material.Id,
                command.Request.DraftId,
                _userScopedData.UserId,
                _userScopedData.IsAdmin));
        }

        // Атомарная публикация: если автор включил «Опубликовать сразу», переводим
        // материал в PUBLISHED в той же транзакции и публикуем MaterialPublished.
        // Pre-check выше гарантировал, что Publish() не отвалится по контенту.
        bool publishedNow = false;
        if (command.Request.PublishOnCreate)
        {
            UnitResult<Error> publishResult = material.Publish();
            if (publishResult.IsFailure)
                return publishResult.Error;

            List<Guid> publishCourseIds = courseId is { } pcid ? [pcid] : [];

            await _outbox.PublishAsync(new MaterialPublished(
                MaterialId: material.Id,
                Title: material.Title.Value,
                AuthorId: material.AuthorId,
                CourseIds: publishCourseIds,
                NotifySubscribers: command.Request.NotifySubscribers));

            publishedNow = true;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // После коммита — инвалидируем curriculum-кэш курсов, к которым привязан
        // материал, чтобы PUBLISHED-материал сразу появился в сайдбаре. Сбой Redis
        // не должен превращаться в 500 (TTL добьёт за пару минут).
        // NOTE: текущий Create-handler привязывает материал максимум к одному курсу
        // (см. courseId resolution выше). Если в будущем добавится multi-course
        // attach в Create, развернуть этот блок в foreach по всем courseIds (как
        // в Update.cs:199-211).
        if (publishedNow && courseId is { } invalidateCourseId)
        {
            try
            {
                await CourseCacheInvalidator.InvalidateAsync(_cache, invalidateCourseId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Curriculum cache invalidation failed after publish-on-create for material {MaterialId} (will expire via TTL)",
                    material.Id);
            }
        }

        _logger.LogInformation(
            "Material {MaterialId} created by {AuthorId} (Kind={Kind}, AccessType={AccessType}, CourseId={CourseId}, ModuleId={ModuleId}, VideoId={VideoId}, PreviewId={PreviewId}, Published={Published})",
            material.Id, material.AuthorId, material.Kind, material.AccessType,
            courseId, command.Request.ModuleId, command.Request.VideoId, command.Request.PreviewId, publishedNow);

        return material.Id;
    }
}
