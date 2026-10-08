using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record RestoreProjectCommand(Guid ProjectId) : ICommand;

public sealed class RestoreProjectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("projects/{projectId:guid}/restore", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid projectId,
                    [FromServices] RestoreProjectHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new RestoreProjectCommand(projectId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class RestoreProjectHandler : ICommandHandler<Guid, RestoreProjectCommand>
{
    private readonly IProjectsRepository _projectsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<RestoreProjectHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RestoreProjectHandler(
        IProjectsRepository projectsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<RestoreProjectHandler> logger,
        UserScopedData userScopedData)
    {
        _projectsRepository = projectsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        RestoreProjectCommand command, CancellationToken cancellationToken)
    {
        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        Project project = projectResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(project.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> restoreResult = project.Restore();
        if (restoreResult.IsFailure)
            return restoreResult.Error;

        await _outbox.PublishAsync(new ProjectRestored(project.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Project {ProjectId} restored from archive", project.Id);

        return project.Id;
    }
}
