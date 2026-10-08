using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record MoveCourseCommand(Guid CourseId, MoveCourseRequest Request) : ICommand;

public sealed class MoveCourseRequestValidator : AbstractValidator<MoveCourseRequest>
{
    public MoveCourseRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.AfterSortKey is not null || x.BeforeSortKey is not null)
            .WithError(GeneralErrors.ValueIsInvalid("AfterSortKey/BeforeSortKey"));
    }
}

public sealed class MoveCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}/move",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid courseId,
                [FromBody] MoveCourseRequest request,
                [FromServices] MoveCourseHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new MoveCourseCommand(courseId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class MoveCourseHandler : ICommandHandler<Guid, MoveCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly OrderingService<Course> _ordering;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MoveCourseRequest> _validator;
    private readonly UserScopedData _userScopedData;
    private readonly HybridCache _cache;
    private readonly ILogger<MoveCourseHandler> _logger;

    public MoveCourseHandler(
        ICoursesRepository coursesRepository,
        OrderingService<Course> ordering,
        ITransactionManager transactionManager,
        IValidator<MoveCourseRequest> validator,
        UserScopedData userScopedData,
        HybridCache cache,
        ILogger<MoveCourseHandler> logger)
    {
        _coursesRepository = coursesRepository;
        _ordering = ordering;
        _transactionManager = transactionManager;
        _validator = validator;
        _userScopedData = userScopedData;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        MoveCourseCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
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

        Result<SortKey, Error> sortKeyResult = await _ordering.ComputeMoveSortKeyAsync(
            c => c.AuthorId == course.AuthorId && c.Id != course.Id,
            command.Request.AfterSortKey, command.Request.BeforeSortKey,
            cancellationToken);
        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        course.UpdateSortKey(sortKeyResult.Value);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation(
            "Course {CourseId} moved by author {AuthorId}", course.Id, course.AuthorId);

        return course.Id;
    }
}
