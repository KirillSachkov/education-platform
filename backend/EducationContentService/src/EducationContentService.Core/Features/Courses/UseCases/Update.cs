using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Domain;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.CourseItems;
using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Domain.Courses;
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

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record UpdateCourseCommand(Guid CourseId, UpdateCourseRequest Request) : ICommand;

public class UpdateCourseCommandValidator : AbstractValidator<UpdateCourseCommand>
{
    public UpdateCourseCommandValidator()
    {
        When(x => x.Request.Title.IsSet, () =>
            RuleFor(x => x.Request.Title.Value).MustBeValueObject(Title.Create));
        When(x => x.Request.Description.IsSet, () =>
            RuleFor(x => x.Request.Description.Value).MustBeValueObject(Description.Create));
        When(x => x.Request.Slug.IsSet, () =>
            RuleFor(x => x.Request.Slug.Value).MustBeValueObject(CourseSlug.Create));
        When(x => x.Request.Kind.IsSet, () =>
            RuleFor(x => x.Request.Kind.Value)
                .Must(k => Enum.TryParse<CourseKind>(k, ignoreCase: true, out _))
                .WithError(EducationErrors.InvalidCourseKind()));
    }
}

public sealed class UpdateCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromBody] UpdateCourseRequest request,
                    [FromServices] UpdateCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new UpdateCourseCommand(courseId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class UpdateCourseHandler : ICommandHandler<Guid, UpdateCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ICourseItemsRepository _courseItemsRepository;
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IValidator<UpdateCourseCommand> _validator;
    private readonly ILogger<UpdateCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public UpdateCourseHandler(
        ICoursesRepository coursesRepository,
        ICourseItemsRepository courseItemsRepository,
        IModuleItemsRepository moduleItemsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        IFileServiceClient fileServiceClient,
        IValidator<UpdateCourseCommand> validator,
        ILogger<UpdateCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _courseItemsRepository = courseItemsRepository;
        _moduleItemsRepository = moduleItemsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _fileServiceClient = fileServiceClient;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(UpdateCourseCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UpdateCourseRequest request = command.Request;

        // Verify the new starting module belongs to this course before mutating state.
        if (request.GettingStartedModuleId.IsSet && request.GettingStartedModuleId.Value is { } moduleId)
        {
            Result<CourseItem, Error> itemResult = await _courseItemsRepository.GetByAsync(
                ci => ci.CourseId == command.CourseId
                      && ci.ReferenceId == moduleId
                      && ci.ItemType == CourseItemType.Module,
                cancellationToken: cancellationToken);

            if (itemResult.IsFailure)
                return EducationErrors.ItemNotFound("Course", moduleId);
        }

        // Validate slug uniqueness up-front so we don't make remote FileService calls
        // for a request that will fail anyway.
        if (request.Slug.IsSet)
        {
            CourseSlug slugValue = CourseSlug.Create(request.Slug.Value).Value;
            Guid courseId = command.CourseId;
            bool slugExists = await _coursesRepository.ExistsAsync(
                c => c.Slug == slugValue && c.Id != courseId, cancellationToken);
            if (slugExists)
                return EducationErrors.CourseSlugAlreadyExists(slugValue.Value);
        }

        // Capture media ids BEFORE we mutate the aggregate so the diff doesn't
        // depend on the order in which fields are touched below.
        Guid? previousImageId = course.ImageId?.Value;
        Guid? previousVideoId = course.VideoId?.Value;
        long previousImageBindingRevision = course.ImageBindingRevision;
        long previousVideoBindingRevision = course.VideoBindingRevision;

        if (request.Title.IsSet)
            course.SetTitle(Title.Create(request.Title.Value).Value);

        if (request.Description.IsSet)
            course.SetDescription(Description.Create(request.Description.Value).Value);

        if (request.LearningOutcomes.IsSet)
            course.SetLearningOutcomes(request.LearningOutcomes.Value ?? []);

        if (request.TargetAudience.IsSet)
            course.SetTargetAudience(request.TargetAudience.Value ?? []);

        if (request.Prerequisites.IsSet)
            course.SetPrerequisites(request.Prerequisites.Value ?? []);

        if (request.ShowInFullAccess.IsSet)
            course.SetShowInFullAccess(request.ShowInFullAccess.Value);

        // Тип курса корректируется автором/модератором (#640). Парс безопасен — валидатор
        // уже отверг невалидное значение. No-op (тот же Kind) пропускаем, чтобы не дёргать
        // лишний DB-запрос и outbox. INTENSIVE/MARATHON не держат заданий в модулях — если
        // такие есть, смену отклоняем (зеркало AttachIssueToModule).
        if (request.Kind.IsSet)
        {
            CourseKind newKind = Enum.Parse<CourseKind>(request.Kind.Value, ignoreCase: true);
            if (newKind != course.Kind)
            {
                if (newKind is CourseKind.INTENSIVE or CourseKind.MARATHON
                    && await _moduleItemsRepository.HasIssueItemsInCourseAsync(course.Id, cancellationToken))
                {
                    return EducationErrors.CourseKindChangeBlockedByIssues();
                }

                course.ChangeKind(newKind);
            }
        }

        if (request.GettingStartedModuleId.IsSet)
            course.SetGettingStartedModule(request.GettingStartedModuleId.Value);

        if (request.Slug.IsSet)
            course.SetSlug(CourseSlug.Create(request.Slug.Value).Value);

        TargetEntityDto target = new("course", course.Id);
        if (request.PreviewId.IsSet)
        {
            UnitResult<Error> previewSync = await MediaAssetSync.SyncSingleAssetAsync(
                _fileServiceClient,
                _outbox,
                previous: previousImageId,
                previousBindingRevision: previousImageBindingRevision,
                requested: request.PreviewId.Value,
                target: target,
                actorUserId: _userScopedData.UserId,
                actorCanManageAnyAsset: _userScopedData.IsAdmin,
                attach: (id, revision) => course.AttachImage(ImageId.Create(id).Value, revision),
                detach: course.DetachImage,
                cancellationToken);
            if (previewSync.IsFailure)
                return previewSync.Error;
        }

        if (request.VideoId.IsSet)
        {
            UnitResult<Error> videoSync = await MediaAssetSync.SyncSingleAssetAsync(
                _fileServiceClient,
                _outbox,
                previous: previousVideoId,
                previousBindingRevision: previousVideoBindingRevision,
                requested: request.VideoId.Value,
                target: target,
                actorUserId: _userScopedData.UserId,
                actorCanManageAnyAsset: _userScopedData.IsAdmin,
                attach: (id, revision) => course.AttachVideo(VideoId.Create(id).Value, revision),
                detach: course.DetachVideo,
                cancellationToken);
            if (videoSync.IsFailure)
                return videoSync.Error;
        }

        await _outbox.PublishAsync(new CourseUpdated(course.Id));

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, command.CourseId, cancellationToken);

        _logger.LogInformation("Course {CourseId} updated", course.Id);

        return course.Id;
    }
}
