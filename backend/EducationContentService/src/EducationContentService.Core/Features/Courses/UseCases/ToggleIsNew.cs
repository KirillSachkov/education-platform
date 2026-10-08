using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record ToggleIsNewCommand(Guid CourseId, bool IsNew) : ICommand;

public sealed class ToggleIsNewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}/is-new", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromBody] ToggleIsNewRequest request,
                    [FromServices] ToggleIsNewHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ToggleIsNewCommand(courseId, request.IsNew), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed record ToggleIsNewRequest(bool IsNew);

public sealed class ToggleIsNewHandler : ICommandHandler<Guid, ToggleIsNewCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly ILogger<ToggleIsNewHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public ToggleIsNewHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        HybridCache cache,
        ILogger<ToggleIsNewHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(ToggleIsNewCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        course.SetIsNew(command.IsNew);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation(
            "User {UserId} toggled IsNew to {IsNew} for course {CourseId}",
            _userScopedData.UserId, command.IsNew, course.Id);

        return course.Id;
    }
}
