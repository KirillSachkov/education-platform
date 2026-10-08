using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Tracks;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Features.Tracks.UseCases;

public sealed record UpdateTrackCommand(
    Guid TrackId,
    string? Title,
    string? Stack,
    string? Description) : ICommand;

public sealed class UpdateTrackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPut("/trainer/tracks/{trackId:guid}",
                async Task<EndpointResult> (
                    Guid trackId,
                    UpdateTrackRequest request,
                    UpdateTrackHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(
                        new UpdateTrackCommand(
                            trackId,
                            request.Title,
                            request.Stack,
                            request.Description),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>Обновляет детали трека (admin/seed).</summary>
public sealed class UpdateTrackHandler : ICommandHandler<UpdateTrackCommand>
{
    private readonly ITracksRepository _tracks;
    private readonly ITransactionManager _transactions;

    public UpdateTrackHandler(ITracksRepository tracks, ITransactionManager transactions)
    {
        _tracks = tracks;
        _transactions = transactions;
    }

    public async Task<UnitResult<Error>> Handle(
        UpdateTrackCommand command,
        CancellationToken cancellationToken)
    {
        Result<Track, Error> trackResult = await _tracks.GetByAsync(t => t.Id == command.TrackId, cancellationToken);
        if (trackResult.IsFailure)
            return TrainerServiceErrors.Track.NotFound(command.TrackId);

        if (!TrackStackParser.TryParse(command.Stack, out TrackStack stack))
            return TrainerServiceErrors.Track.InvalidStack(command.Stack ?? string.Empty);

        Track track = trackResult.Value;

        UnitResult<Error> updateResult = track.UpdateDetails(command.Title, stack, command.Description);
        if (updateResult.IsFailure)
            return updateResult.Error;

        return await _transactions.SaveChangesAsync(cancellationToken);
    }
}
