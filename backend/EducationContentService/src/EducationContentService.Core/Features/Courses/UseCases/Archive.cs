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

public sealed record ArchiveCourseCommand(Guid CourseId) : ICommand;

public sealed class ArchiveCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/archive", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromServices] ArchiveCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ArchiveCourseCommand(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class ArchiveCourseHandler : ICommandHandler<Guid, ArchiveCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<ArchiveCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public ArchiveCourseHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<ArchiveCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(ArchiveCourseCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> archiveResult = course.Archive();
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        await _outbox.PublishAsync(new CourseSoftDeleted(course.Id));

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation("Course {CourseId} archived", course.Id);

        return course.Id;
    }
}
