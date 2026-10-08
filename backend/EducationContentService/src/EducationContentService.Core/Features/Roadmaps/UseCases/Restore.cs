using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain.Roadmaps;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Roadmaps.UseCases;

public sealed record RestoreRoadmapCommand(Guid RoadmapId) : ICommand;

public sealed class RestoreRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("roadmaps/{roadmapId:guid}/restore", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromServices] RestoreRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new RestoreRoadmapCommand(roadmapId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class RestoreRoadmapHandler : ICommandHandler<Guid, RestoreRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<RestoreRoadmapHandler> _logger;

    public RestoreRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        ILogger<RestoreRoadmapHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(RestoreRoadmapCommand command, CancellationToken cancellationToken)
    {
        Result<Roadmap, Error> roadmapResult = await _roadmapsRepository.GetByAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmapResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> restoreResult = roadmapResult.Value.Restore();
        if (restoreResult.IsFailure)
            return restoreResult.Error;

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap {RoadmapId} restored", command.RoadmapId);

        return command.RoadmapId;
    }
}
