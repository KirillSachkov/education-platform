using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Tracks;
using TrainerService.Core.Database;
using TrainerService.Domain.Topics;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Features.Tracks.Queries;

public sealed record GetTracksQuery : IQuery;

public sealed class GetTracksEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/tracks",
                async Task<EndpointResult<IReadOnlyList<TrackDto>>> (
                    GetTracksHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetTracksQuery(), cancellationToken))
            // Метаданные треков — не gated (как каталог курсов): аноним просматривает хаб
            // read-only (#614 F). Персональных данных нет — pure metadata + счётчик тем.
            .AllowAnonymousEndpoint();
    }
}

/// <summary>
///     Список PUBLISHED-треков для верхнего селектора хаба + число опубликованных тем в каждом.
///     Метаданные треков — не gated (как каталог курсов).
/// </summary>
public sealed class GetTracksHandler : IQueryHandlerWithResult<IReadOnlyList<TrackDto>, GetTracksQuery>
{
    private readonly ITracksRepository _tracks;
    private readonly ITopicsRepository _topics;

    public GetTracksHandler(ITracksRepository tracks, ITopicsRepository topics)
    {
        _tracks = tracks;
        _topics = topics;
    }

    public async Task<Result<IReadOnlyList<TrackDto>, Error>> Handle(
        GetTracksQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Track> tracks = await _tracks.GetManyByAsync(t => t.IsPublished, cancellationToken);
        if (tracks.Count == 0)
            return new List<TrackDto>();

        var trackIds = tracks.Select(t => t.Id).ToHashSet();

        IReadOnlyList<Topic> publishedTopics =
            await _topics.GetManyByAsync(t => t.IsPublished && trackIds.Contains(t.TrackId), cancellationToken);
        Dictionary<Guid, int> topicCountByTrack = publishedTopics
            .GroupBy(t => t.TrackId)
            .ToDictionary(g => g.Key, g => g.Count());

        return tracks
            .OrderBy(t => t.SortKey, StringComparer.Ordinal)
            .Select(t => new TrackDto(
                t.Id,
                t.Slug,
                t.Title,
                t.Stack.ToString(),
                t.Description,
                topicCountByTrack.GetValueOrDefault(t.Id)))
            .ToList();
    }
}
