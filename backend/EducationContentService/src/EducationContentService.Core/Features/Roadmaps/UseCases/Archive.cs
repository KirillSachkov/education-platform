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

public sealed record ArchiveRoadmapCommand(Guid RoadmapId) : ICommand;

public sealed class ArchiveRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("roadmaps/{roadmapId:guid}/archive", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromServices] ArchiveRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ArchiveRoadmapCommand(roadmapId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class ArchiveRoadmapHandler : ICommandHandler<Guid, ArchiveRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<ArchiveRoadmapHandler> _logger;

    public ArchiveRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        ILogger<ArchiveRoadmapHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(ArchiveRoadmapCommand command, CancellationToken cancellationToken)
    {
        Result<Roadmap, Error> roadmapResult = await _roadmapsRepository.GetByAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmapResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> archiveResult = roadmapResult.Value.Archive();
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap {RoadmapId} archived", command.RoadmapId);

        return command.RoadmapId;
    }
}
