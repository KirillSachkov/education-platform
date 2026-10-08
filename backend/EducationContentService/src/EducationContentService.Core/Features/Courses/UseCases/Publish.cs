using ContentAccess;
using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.ContentAccess;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record PublishCourseCommand(Guid CourseId) : ICommand;

public sealed class PublishCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromServices] PublishCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new PublishCourseCommand(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class PublishCourseHandler : ICommandHandler<Guid, PublishCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IResourceAccessWriter _resourceAccessWriter;
    private readonly HybridCache _cache;
    private readonly ILogger<PublishCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public PublishCourseHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IResourceAccessWriter resourceAccessWriter,
        HybridCache cache,
        ILogger<PublishCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _resourceAccessWriter = resourceAccessWriter;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(PublishCourseCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        bool titleConflict = await _coursesRepository.ExistsByTitleAsync(course.Title, course.Id, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Course", course.Title.Value);

        UnitResult<Error> publishResult = course.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        await _outbox.PublishAsync(new CoursePublished(course.Id));

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        await _resourceAccessWriter.SetTagsAsync(
            ResourceTypes.COURSE,
            course.Id,
            ContentAccessTagBuilder.BuildCourseAccessTags(course.Id),
            cancellationToken);

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation("Course {CourseId} published", course.Id);

        return course.Id;
    }
}

