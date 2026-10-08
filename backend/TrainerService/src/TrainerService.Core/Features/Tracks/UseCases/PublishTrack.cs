using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Features.Tracks.UseCases;

public sealed record PublishTrackCommand(Guid TrackId) : ICommand;

public sealed class PublishTrackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/trainer/tracks/{trackId:guid}/publish",
                async Task<EndpointResult> (
                    Guid trackId,
                    PublishTrackHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new PublishTrackCommand(trackId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Публикует трек — делает его видимым в студенческом верхнем селекторе (admin/seed).</summary>
public sealed class PublishTrackHandler : ICommandHandler<PublishTrackCommand>
{
    private readonly ITracksRepository _tracks;
    private readonly ITransactionManager _transactions;

    public PublishTrackHandler(ITracksRepository tracks, ITransactionManager transactions)
    {
        _tracks = tracks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        PublishTrackCommand command,
        CancellationToken cancellationToken)
    {
        Result<Track, Error> trackResult = await _tracks.GetByAsync(t => t.Id == command.TrackId, cancellationToken);
        if (trackResult.IsFailure)
            return TrainerServiceErrors.Track.NotFound(command.TrackId);

        trackResult.Value.Publish();

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
