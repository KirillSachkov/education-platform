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

public sealed record DetachProjectIssueCommand(Guid ProjectId, Guid IssueId) : ICommand;

public sealed class DetachProjectIssueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("projects/{projectId:guid}/issues/{issueId:guid}",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid projectId,
                [FromRoute] Guid issueId,
                [FromServices] DetachProjectIssueHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new DetachProjectIssueCommand(projectId, issueId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class DetachProjectIssueHandler : ICommandHandler<Guid, DetachProjectIssueCommand>
{
    private readonly IProjectItemsRepository _projectItemsRepository;
    private readonly IProjectsRepository _projectsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<DetachProjectIssueHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DetachProjectIssueHandler(
        IProjectItemsRepository projectItemsRepository,
        IProjectsRepository projectsRepository,
        ITransactionManager transactionManager,
        ILogger<DetachProjectIssueHandler> logger,
        UserScopedData userScopedData)
    {
        _projectItemsRepository = projectItemsRepository;
        _projectsRepository = projectsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        DetachProjectIssueCommand command, CancellationToken cancellationToken)
    {
        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(projectResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<ProjectItem, Error> itemResult = await _projectItemsRepository.GetByAsync(
            pi => pi.ProjectId == command.ProjectId && pi.IssueId == command.IssueId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        ProjectItem item = itemResult.Value;

        _projectItemsRepository.Delete(item);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Issue {IssueId} detached from project {ProjectId}",
            command.IssueId, command.ProjectId);

        return item.Id;
    }
}
