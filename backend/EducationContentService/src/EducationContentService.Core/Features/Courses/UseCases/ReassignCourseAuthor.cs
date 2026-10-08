using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Courses.UseCases;

/// <summary>
///     Admin/moderator ownership transfer (issue #587). Reassigns a course AND the content
///     owned exclusively by it (modules, projects, issues, materials, quizzes, course-level
///     collections) to a new author, so the new author fully owns it, can manage it, and
///     receives "from users" notifications for it. Materials/quizzes shared with other
///     courses are kept under the previous author. Access is intentionally NOT changed —
///     existing students keep access via the course-plan, which is author-agnostic (#589).
/// </summary>
public sealed record ReassignCourseAuthorCommand(Guid CourseId, Guid NewAuthorId) : ICommand;

public sealed class ReassignCourseAuthorEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}/author", async Task<EndpointResult<ReassignCourseAuthorResponse>> (
                    [FromRoute] Guid courseId,
                    [FromBody] ReassignCourseAuthorRequest request,
                    [FromServices] ReassignCourseAuthorHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new ReassignCourseAuthorCommand(courseId, request.NewAuthorId), cancellationToken))
            // Передача владения — операция админа/модератора контента (content.moderate
            // bypass'ит Tier-2 ownership). Обычный автор не должен забирать/раздавать курсы.
            .RequirePermissions(PlatformPermissions.Content.MODERATE);
    }
}

public sealed class ReassignCourseAuthorHandler
    : ICommandHandler<ReassignCourseAuthorResponse, ReassignCourseAuthorCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly OrderingService<Course> _ordering;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<ReassignCourseAuthorHandler> _logger;

    public ReassignCourseAuthorHandler(
        ICoursesRepository coursesRepository,
        OrderingService<Course> ordering,
        IOutboxService outbox,
        ITransactionManager transactionManager,
        HybridCache cache,
        UserScopedData userScopedData,
        ILogger<ReassignCourseAuthorHandler> logger)
    {
        _coursesRepository = coursesRepository;
        _ordering = ordering;
        _outbox = outbox;
        _transactionManager = transactionManager;
        _cache = cache;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<ReassignCourseAuthorResponse, Error>> Handle(
        ReassignCourseAuthorCommand command, CancellationToken cancellationToken)
    {
        if (command.NewAuthorId == Guid.Empty)
            return GeneralErrors.ValueIsInvalid("newAuthorId");

        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        if (course.AuthorId == command.NewAuthorId)
            return EducationErrors.CourseAuthorUnchanged();

        Guid previousAuthorId = course.AuthorId;

        UnitResult<Error> begin = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (begin.IsFailure)
            return begin.Error;

        // Append the course to the END of the new author's list (SortKey is author-scoped) —
        // computed BEFORE the flip so the course itself is excluded from the new author's set.
        SortKey newSortKey = await _ordering.ComputeAppendSortKeyAsync(
            c => c.AuthorId == command.NewAuthorId, cancellationToken);

        long ownershipRevision = await _coursesRepository.GetNextAssetOwnershipRevisionAsync(
            cancellationToken);
        course.ReassignAuthor(command.NewAuthorId, ownershipRevision);
        course.UpdateSortKey(newSortKey);

        // Flush the course-root flip BEFORE the raw-SQL child flips so the DB sees a consistent
        // author across course + children mid-transaction (both still commit atomically).
        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
            return save.Error;

        CourseAuthorReassignmentResult reassignment =
            await _coursesRepository.ReassignChildContentAuthorAsync(
                course.Id, command.NewAuthorId, cancellationToken);

        await _outbox.PublishAsync(new CourseAssetOwnershipChanged(
            course.Id,
            course.AssetOwnershipRevision,
            command.NewAuthorId,
            BuildAssetTargets(course.Id, reassignment)));

        UnitResult<Error> commit = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commit.IsFailure)
            return commit.Error;

        // Курс уходит из /courses/my прежнего автора и появляется у нового: чистим
        // curriculum/landing курса, общий каталог и портфолио ОБОИХ авторов.
        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);
        await _cache.RemoveByTagAsync(GetCatalogHandler.CATALOG_CACHE_TAG, cancellationToken);
        await _cache.RemoveByTagAsync(
            $"{GetAuthorCoursesHandler.AuthorCacheTagPrefix}:{previousAuthorId}", cancellationToken);
        await _cache.RemoveByTagAsync(
            $"{GetAuthorCoursesHandler.AuthorCacheTagPrefix}:{command.NewAuthorId}", cancellationToken);

        _logger.LogInformation(
            "Course {CourseId} ownership transferred {PreviousAuthorId} -> {NewAuthorId} by {ActorId}; "
            + "skipped {SharedMaterials} shared materials, {SharedQuizzes} shared quizzes",
            course.Id, previousAuthorId, command.NewAuthorId, _userScopedData.UserId,
            reassignment.SkippedSharedMaterialIds.Count, reassignment.SkippedSharedQuizIds.Count);

        return new ReassignCourseAuthorResponse(
            course.Id,
            command.NewAuthorId,
            reassignment.SkippedSharedMaterialIds,
            reassignment.SkippedSharedQuizIds);
    }

    private static IReadOnlyList<AssetOwnershipTarget> BuildAssetTargets(
        Guid courseId,
        CourseAuthorReassignmentResult reassignment) =>
        [
            new("course", courseId),
            .. reassignment.TransferredProjectIds.Select(id => new AssetOwnershipTarget("project", id)),
            .. reassignment.TransferredIssueIds.Select(id => new AssetOwnershipTarget("issue", id)),
            .. reassignment.TransferredMaterialIds.Select(id => new AssetOwnershipTarget("material", id)),
            .. reassignment.TransferredCollectionIds.Select(id => new AssetOwnershipTarget("collection", id)),
        ];
}
