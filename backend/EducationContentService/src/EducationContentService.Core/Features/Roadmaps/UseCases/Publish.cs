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

public sealed record PublishRoadmapCommand(Guid RoadmapId) : ICommand;

public sealed class PublishRoadmapEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("roadmaps/{roadmapId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid roadmapId,
                    [FromServices] PublishRoadmapHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new PublishRoadmapCommand(roadmapId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class PublishRoadmapHandler : ICommandHandler<Guid, PublishRoadmapCommand>
{
    private readonly IRoadmapsRepository _roadmapsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<PublishRoadmapHandler> _logger;

    public PublishRoadmapHandler(
        IRoadmapsRepository roadmapsRepository,
        ITransactionManager transactionManager,
        UserScopedData userScopedData,
        ILogger<PublishRoadmapHandler> logger)
    {
        _roadmapsRepository = roadmapsRepository;
        _transactionManager = transactionManager;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(PublishRoadmapCommand command, CancellationToken cancellationToken)
    {
        Result<Roadmap, Error> roadmapResult = await _roadmapsRepository.GetByAsync(
            r => r.Id == command.RoadmapId, cancellationToken);
        if (roadmapResult.IsFailure)
            return roadmapResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(roadmapResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> publishResult = roadmapResult.Value.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        _logger.LogInformation("Roadmap {RoadmapId} published", command.RoadmapId);

        return command.RoadmapId;
    }
}
