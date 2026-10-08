using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.CourseItems.UseCases;

public sealed record DetachCourseItemCommand(Guid CourseId, Guid ReferenceId) : ICommand;

public sealed class DetachCourseItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("courses/{courseId:guid}/items/{referenceId:guid}",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid courseId,
                [FromRoute] Guid referenceId,
                [FromServices] DetachCourseItemHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new DetachCourseItemCommand(courseId, referenceId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class DetachCourseItemHandler : ICommandHandler<Guid, DetachCourseItemCommand>
{
    private readonly ICourseItemsRepository _courseItemsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<DetachCourseItemHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DetachCourseItemHandler(
        ICourseItemsRepository courseItemsRepository,
        ICoursesRepository coursesRepository,
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<DetachCourseItemHandler> logger,
        UserScopedData userScopedData)
    {
        _courseItemsRepository = courseItemsRepository;
        _coursesRepository = coursesRepository;
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        DetachCourseItemCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<CourseItem, Error> itemResult = await _courseItemsRepository.GetByAsync(
            ci => ci.CourseId == command.CourseId && ci.ReferenceId == command.ReferenceId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        CourseItem item = itemResult.Value;

        IReadOnlyList<Guid> affectedIssueIds = await _issuesRepository.GetIdsByCourseItemAsync(
            item.ItemType,
            item.ReferenceId,
            cancellationToken);

        _courseItemsRepository.Delete(item);

        foreach (Guid issueId in affectedIssueIds)
            await _outbox.PublishAsync(new IssueAccessChanged(issueId));

        courseResult.Value.ClearGettingStartedModuleIfMatches(command.ReferenceId);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, command.CourseId, cancellationToken);

        _logger.LogInformation(
            "Item {ReferenceId} detached from course {CourseId}",
            command.ReferenceId, command.CourseId);

        return item.Id;
    }
}
