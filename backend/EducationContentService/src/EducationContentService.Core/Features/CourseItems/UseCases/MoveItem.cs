using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Courses;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.CourseItems.UseCases;

public sealed record MoveCourseItemCommand(
    Guid CourseId, Guid ReferenceId, MoveCourseItemRequest Request) : ICommand;

public class MoveCourseItemRequestValidator : AbstractValidator<MoveCourseItemRequest>
{
    public MoveCourseItemRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.AfterSortKey is not null || x.BeforeSortKey is not null)
            .WithError(GeneralErrors.ValueIsInvalid("AfterSortKey/BeforeSortKey"));
    }
}

public sealed class MoveCourseItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}/items/{referenceId:guid}/move",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid courseId,
                [FromRoute] Guid referenceId,
                [FromBody] MoveCourseItemRequest request,
                [FromServices] MoveCourseItemHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new MoveCourseItemCommand(courseId, referenceId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class MoveCourseItemHandler : ICommandHandler<Guid, MoveCourseItemCommand>
{
    private readonly ICourseItemsRepository _courseItemsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly CourseItemService _courseItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<MoveCourseItemRequest> _validator;
    private readonly ILogger<MoveCourseItemHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public MoveCourseItemHandler(
        ICourseItemsRepository courseItemsRepository,
        ICoursesRepository coursesRepository,
        CourseItemService courseItemService,
        ITransactionManager transactionManager,
        IValidator<MoveCourseItemRequest> validator,
        ILogger<MoveCourseItemHandler> logger,
        UserScopedData userScopedData)
    {
        _courseItemsRepository = courseItemsRepository;
        _coursesRepository = coursesRepository;
        _courseItemService = courseItemService;
        _transactionManager = transactionManager;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        MoveCourseItemCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        // Find the course item by courseId + referenceId
        Result<CourseItem, Error> itemResult = await _courseItemsRepository.GetByAsync(
            ci => ci.CourseId == command.CourseId && ci.ReferenceId == command.ReferenceId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        CourseItem item = itemResult.Value;

        Result<SortKey, Error> sortKeyResult = await _courseItemService.ComputeMoveSortKey(
            command.CourseId, command.ReferenceId,
            command.Request.AfterSortKey, command.Request.BeforeSortKey,
            cancellationToken);
        if (sortKeyResult.IsFailure)
            return sortKeyResult.Error;

        item.UpdateSortKey(sortKeyResult.Value);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Course item {ReferenceId} moved in course {CourseId}",
            command.ReferenceId, command.CourseId);

        return item.Id;
    }
}
