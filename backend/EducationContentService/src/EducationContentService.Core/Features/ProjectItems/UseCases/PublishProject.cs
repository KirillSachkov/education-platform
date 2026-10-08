using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain;
using EducationContentService.Domain.Projects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ProjectItems.UseCases;

public sealed record PublishProjectCommand(Guid ProjectId) : ICommand;

public sealed class PublishProjectEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("projects/{projectId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid projectId,
                    [FromServices] PublishProjectHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new PublishProjectCommand(projectId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Issues.MANAGE);
    }
}

public sealed class PublishProjectHandler : ICommandHandler<Guid, PublishProjectCommand>
{
    private readonly IProjectsRepository _projectsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<PublishProjectHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public PublishProjectHandler(
        IProjectsRepository projectsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<PublishProjectHandler> logger,
        UserScopedData userScopedData)
    {
        _projectsRepository = projectsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        PublishProjectCommand command, CancellationToken cancellationToken)
    {
        Result<Project, Error> projectResult = await _projectsRepository.GetByAsync(
            p => p.Id == command.ProjectId, cancellationToken);
        if (projectResult.IsFailure)
            return projectResult.Error;

        Project project = projectResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(project.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        bool titleConflict = await _projectsRepository.ExistsByTitleAsync(project.Title, project.Id, cancellationToken);
        if (titleConflict)
            return EducationErrors.TitleAlreadyExists("Project", project.Title.Value);

        UnitResult<Error> publishResult = project.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        await _outbox.PublishAsync(new ProjectPublished(project.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Project {ProjectId} published", project.Id);

        return project.Id;
    }
}
