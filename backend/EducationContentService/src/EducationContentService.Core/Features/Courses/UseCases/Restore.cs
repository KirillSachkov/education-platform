using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
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

public sealed record RestoreCourseCommand(Guid CourseId) : ICommand;

public sealed class RestoreCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/restore", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromServices] RestoreCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new RestoreCourseCommand(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class RestoreCourseHandler : ICommandHandler<Guid, RestoreCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<RestoreCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RestoreCourseHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<RestoreCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(RestoreCourseCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> restoreResult = course.Restore();
        if (restoreResult.IsFailure)
            return restoreResult.Error;

        await _outbox.PublishAsync(new CourseRestored(course.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation("Course {CourseId} restored from archive", course.Id);

        return course.Id;
    }
}
