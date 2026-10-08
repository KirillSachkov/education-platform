using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record AttachIssueToProjectCommand(Guid ProjectId, Guid IssueId) : ICommand;

public sealed class AttachIssueToProjectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("projects/{projectId:guid}/issues/{issueId:guid}/attach",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid projectId,
                    [FromRoute] Guid issueId,
                    [FromServices] AttachIssueToProjectHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new AttachIssueToProjectCommand(projectId, issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class AttachIssueToProjectHandler : ICommandHandler<Guid, AttachIssueToProjectCommand>
{
    private readonly IProjectsRepository _projectsRepository;
    private readonly IIssuesRepository _issuesRepository;
    private readonly IProjectItemsRepository _projectItemsRepository;
    private readonly ProjectItemService _projectItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<AttachIssueToProjectHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public AttachIssueToProjectHandler(
        IProjectsRepository projectsRepository,
        IIssuesRepository issuesRepository,
        IProjectItemsRepository projectItemsRepository,
        ProjectItemService projectItemService,
        ITransactionManager transactionManager,
        ILogger<AttachIssueToProjectHandler> logger,
        UserScopedData userScopedData)
    {
        _projectsRepository = projectsRepository;
        _issuesRepository = issuesRepository;
        _projectItemsRepository = projectItemsRepository;
        _projectItemService = projectItemService;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        AttachIssueToProjectCommand command, CancellationToken cancellationToken)
    {
        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        UnitResult<Error> projectOwnership = _userScopedData.CheckOwnership(projectResult.Value.AuthorId);
        if (projectOwnership.IsFailure)
            return projectOwnership.Error;

        Result<Issue, Error> issueResult = await _issuesRepository.GetByAsync(
            i => i.Id == command.IssueId, cancellationToken);
        if (issueResult.IsFailure)
            return issueResult.Error;

        UnitResult<Error> issueOwnership = _userScopedData.CheckOwnership(issueResult.Value.AuthorId);
        if (issueOwnership.IsFailure)
            return issueOwnership.Error;

        // Idempotent: if already bound anywhere, decide based on target project.
        List<ProjectItem> existingBindings = await _projectItemsRepository.GetManyByAsync(
            pi => pi.IssueId == command.IssueId,
            cancellationToken);

        ProjectItem? alreadyInTarget = existingBindings
            .FirstOrDefault(pi => pi.ProjectId == command.ProjectId);
        if (alreadyInTarget is not null)
            return alreadyInTarget.Id;

        // An issue can be attached to at most one project at a time.
        if (existingBindings.Count > 0)
            return Error.Conflict(
                "issue.project.already_bound",
                $"Issue {command.IssueId} is already attached to another project. Detach first.");

        Result<ProjectItem, Error> itemResult = await _projectItemService.CreateAsync(
            command.ProjectId, command.IssueId, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} attached to project {ProjectId}",
            command.IssueId, command.ProjectId);

        return itemResult.Value.Id;
    }
}
