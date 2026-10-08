using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Projects;
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

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record CreateProjectIssueCommand(
    Guid ProjectId, CreateProjectIssueRequest Request) : ICommand;

public class CreateProjectIssueRequestValidator : AbstractValidator<CreateProjectIssueRequest>
{
    public CreateProjectIssueRequestValidator()
    {
        RuleFor(x => x.Title).MustBeValueObject(Title.Create);
        RuleFor(x => x.Content).MustBeValueObject(MarkdownContent.Create);
        RuleFor(x => x.SubmissionMode)
            .Must(value => Enum.TryParse<IssueSubmissionMode>(value, true, out _))
            .WithMessage("Неверный режим сдачи задачи.");
        RuleFor(x => x.SelfCheckInstructions)
            .MaximumLength(MarkdownContent.MAX_LENGTH)
            .When(x => !string.IsNullOrWhiteSpace(x.SelfCheckInstructions));
    }
}

public sealed class CreateProjectIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("projects/{projectId:guid}/issues", async Task<EndpointResult<Guid>> (
            [FromRoute] Guid projectId,
            [FromBody] CreateProjectIssueRequest request,
            [FromServices] CreateProjectIssueHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(
                new CreateProjectIssueCommand(projectId, request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class CreateProjectIssueHandler : ICommandHandler<Guid, CreateProjectIssueCommand>
{
    private readonly IProjectsRepository _projectsRepository;
    private readonly IIssuesRepository _issuesRepository;
    private readonly ProjectItemService _projectItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<CreateProjectIssueRequest> _validator;
    private readonly ILogger<CreateProjectIssueHandler> _logger;
    private readonly UserScopedData _user;

    public CreateProjectIssueHandler(
        IProjectsRepository projectsRepository,
        IIssuesRepository issuesRepository,
        ProjectItemService projectItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<CreateProjectIssueRequest> validator,
        ILogger<CreateProjectIssueHandler> logger,
        UserScopedData user)
    {
        _projectsRepository = projectsRepository;
        _issuesRepository = issuesRepository;
        _projectItemService = projectItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _user = user;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateProjectIssueCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        UnitResult<Error> ownership = _user.CheckOwnership(projectResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Title title = Title.Create(command.Request.Title).Value;
        MarkdownContent content = MarkdownContent.Create(command.Request.Content).Value;
        IssueSubmissionMode submissionMode =
            Enum.Parse<IssueSubmissionMode>(command.Request.SubmissionMode, true);

        var issue = new Issue(
            _user.UserId,
            command.ProjectId,
            title,
            content,
            submissionMode: submissionMode,
            selfCheckInstructions: command.Request.SelfCheckInstructions);

        await _issuesRepository.AddAsync(issue, cancellationToken);

        Result<ProjectItem, Error> itemResult = await _projectItemService.CreateAsync(
            command.ProjectId, issue.Id, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        await _outbox.PublishAsync(new IssueCreated(issue.Id, Guid.Empty));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} created and bound to project {ProjectId}",
            issue.Id, command.ProjectId);

        return issue.Id;
    }
}
