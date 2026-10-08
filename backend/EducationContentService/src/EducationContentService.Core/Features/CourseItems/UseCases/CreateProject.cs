using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
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

public sealed record CreateCourseProjectCommand(Guid CourseId, CreateCourseProjectRequest Request) : ICommand;

public class CreateCourseProjectRequestValidator : AbstractValidator<CreateCourseProjectRequest>
{
    public CreateCourseProjectRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Description).MustBeValueObject(v => Description.Create(v!))
            .When(x => !string.IsNullOrWhiteSpace(x.Description));
        When(x => !string.IsNullOrWhiteSpace(x.DetailedDescription), () =>
        {
            RuleFor(x => x.DetailedDescription!)
                .MustBeValueObject(DetailedDescription.Create);
        });
        When(x => x.IsAutoReviewEnabled, () =>
        {
            RuleFor(x => x.RequiresGithubConnection)
                .Equal(true)
                .WithMessage("AI-проверка требует привязку GitHub.");
            RuleFor(x => x.RequiresReviewApp)
                .Equal(true)
                .WithMessage("AI-проверка требует GitHub App / review bot.");
        });
    }
}

public sealed class CreateCourseProjectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("courses/{courseId:guid}/projects", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid courseId,
            [FromBody] CreateCourseProjectRequest request,
            [FromServices] CreateCourseProjectHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new CreateCourseProjectCommand(courseId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class CreateCourseProjectHandler : ICommandHandler<Guid, CreateCourseProjectCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly IProjectsRepository _projectsRepository;
    private readonly IReviewConfigRepository _reviewConfigRepository;
    private readonly CourseItemService _courseItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<CreateCourseProjectRequest> _validator;
    private readonly ILogger<CreateCourseProjectHandler> _logger;
    private readonly UserScopedData _user;

    public CreateCourseProjectHandler(
        ICoursesRepository coursesRepository,
        IProjectsRepository projectsRepository,
        IReviewConfigRepository reviewConfigRepository,
        CourseItemService courseItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<CreateCourseProjectRequest> validator,
        ILogger<CreateCourseProjectHandler> logger,
        UserScopedData user)
    {
        _coursesRepository = coursesRepository;
        _projectsRepository = projectsRepository;
        _reviewConfigRepository = reviewConfigRepository;
        _courseItemService = courseItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateCourseProjectCommand command, CancellationToken cancellationToken)
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

        // Create project aggregate
        Title title = Title.Create(command.Request.Title).Value;
        Description? description = string.IsNullOrWhiteSpace(command.Request.Description)
            ? null
            : Description.Create(command.Request.Description).Value;
        DetailedDescription? detailedDescription = string.IsNullOrWhiteSpace(command.Request.DetailedDescription)
            ? null
            : DetailedDescription.Create(command.Request.DetailedDescription).Value;

        var project = new Project(_user.UserId, title, description, detailedDescription);

        await _projectsRepository.AddAsync(project, cancellationToken);

        ProjectReviewContext reviewContext = ProjectReviewContext.Create(
            project.Id,
            guidelinesMarkdown: string.Empty,
            command.Request.IsAutoReviewEnabled,
            command.Request.RequiresGithubConnection,
            command.Request.RequiresReviewApp);
        await _reviewConfigRepository.AddProjectReviewContextAsync(reviewContext, cancellationToken);

        // Create CourseItem linking project to course
        Result<CourseItem, Error> itemResult = await _courseItemService.CreateAsync(
            command.CourseId, CourseItemType.Project, project.Id, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        await _outbox.PublishAsync(new ProjectCreated(project.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Project {ProjectId} created and bound to course {CourseId}",
            project.Id, command.CourseId);

        return project.Id;
    }
}
