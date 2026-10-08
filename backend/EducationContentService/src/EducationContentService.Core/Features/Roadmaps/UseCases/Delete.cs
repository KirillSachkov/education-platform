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

public sealed record DeleteRoadmapCommand(Guid RoadmapId) : ICommand;

public sealed class DeleteRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("roadmaps/{roadmapId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromServices] DeleteRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteRoadmapCommand(roadmapId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class DeleteRoadmapHandler : ICommandHandler<Guid, DeleteRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<DeleteRoadmapHandler> _logger;

    public DeleteRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        ILogger<DeleteRoadmapHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteRoadmapCommand command, CancellationToken cancellationToken)
    {
        Result<Roadmap, Error> roadmapResult = await _roadmapsRepository.GetByAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmapResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        await _roadmapsRepository.DeleteAsync(roadmapResult.Value, cancellationToken);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap {RoadmapId} deleted", command.RoadmapId);

        return command.RoadmapId;
    }
}
