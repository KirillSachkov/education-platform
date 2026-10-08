using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Core.Features.ModuleItems;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.CourseItems.UseCases;

public sealed record CreateCourseModuleCommand(Guid CourseId, CreateCourseModuleRequest Request) : ICommand;

public class CreateCourseModuleRequestValidator : AbstractValidator<CreateCourseModuleRequest>
{
    public CreateCourseModuleRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Description).MustBeValueObject(v => Description.Create(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
    }
}

public sealed class CreateCourseModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/modules", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid courseId,
            [FromBody] CreateCourseModuleRequest request,
            [FromServices] CreateCourseModuleHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new CreateCourseModuleCommand(courseId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class CreateCourseModuleHandler : ICommandHandler<Guid, CreateCourseModuleCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly CourseItemService _courseItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<CreateCourseModuleRequest> _validator;
    private readonly ILogger<CreateCourseModuleHandler> _logger;
    private readonly UserScopedData _user;

    public CreateCourseModuleHandler(
        ICoursesRepository coursesRepository,
        IModulesRepository modulesRepository,
        CourseItemService courseItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<CreateCourseModuleRequest> validator,
        ILogger<CreateCourseModuleHandler> logger,
        UserScopedData user)
    {
        _coursesRepository = coursesRepository;
        _modulesRepository = modulesRepository;
        _courseItemService = courseItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateCourseModuleCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        // Verify course exists
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> ownership = _user.CheckOwnership(courseResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        // Create module aggregate
        Title title = Title.Create(command.Request.Title).Value;
        Description? description = string.IsNullOrWhiteSpace(command.Request.Description)
            ? null
            : Description.Create(command.Request.Description).Value;

        var module = new Module(_user.UserId, title, description);

        await _modulesRepository.AddAsync(module, cancellationToken);

        // Create CourseItem linking module to course
        Result<CourseItem, Error> itemResult = await _courseItemService.CreateAsync(
            command.CourseId, CourseItemType.Module, module.Id, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        await _outbox.PublishAsync(new ModuleCreated(module.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Module {ModuleId} created and bound to course {CourseId}",
            module.Id, command.CourseId);

        return module.Id;
    }
}
