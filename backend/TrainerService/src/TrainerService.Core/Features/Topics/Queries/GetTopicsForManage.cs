using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain.TopicBanks;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Topics.Queries;

public sealed record GetTopicsForManageQuery(Guid? TrackId) : IQuery;

public sealed class GetTopicsForManageEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/topics/manage",
                async Task<EndpointResult<IReadOnlyList<TopicAdminDto>>> (
                    GetTopicsForManageHandler handler,
                    CancellationToken cancellationToken,
                    Guid? trackId = null) =>
                    await handler.Handle(new GetTopicsForManageQuery(trackId), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Admin-список тем (включая DRAFT) для редактора автора, опционально по треку. Не скрывает
///     неопубликованные (в отличие от студенческого <c>GET /trainer/topics</c>). Несёт счётчик
///     привязанных банков, чтобы UI понимал, можно ли удалить тему.
/// </summary>
public sealed class GetTopicsForManageHandler
    : IQueryHandlerWithResult<IReadOnlyList<TopicAdminDto>, GetTopicsForManageQuery>
{
    private readonly ITopicsRepository _topics;
    private readonly ITopicBanksRepository _banks;

    public GetTopicsForManageHandler(ITopicsRepository topics, ITopicBanksRepository banks)
    {
        _topics = topics;
        _banks = banks;
    }

    public async Task<Result<IReadOnlyList<TopicAdminDto>, Error>> Handle(
        GetTopicsForManageQuery query,
        CancellationToken cancellationToken)
    {
        Guid? trackId = query.TrackId is { } id && id != Guid.Empty ? id : null;

        IReadOnlyList<Topic> topics = await _topics.GetManyByAsync(
            t => trackId == null || t.TrackId == trackId,
            cancellationToken);
        if (topics.Count == 0)
            return new List<TopicAdminDto>();

        var topicIds = topics.Select(t => t.Id).ToHashSet();
        IReadOnlyList<TopicBank> banks =
            await _banks.GetManyByAsync(b => topicIds.Contains(b.TopicId), cancellationToken);
        Dictionary<Guid, int> bankCountByTopic = banks
            .GroupBy(b => b.TopicId)
            .ToDictionary(g => g.Key, g => g.Count());

        return topics
            .OrderBy(t => t.SortKey, StringComparer.Ordinal)
            .Select(t => new TopicAdminDto(
                t.Id,
                t.TrackId,
                t.Slug,
                t.Title,
                t.Area,
                t.Description,
                t.Direction?.ToString(),
                t.RecommendedCourseId,
                t.FallbackCourseId,
                t.SortKey,
                t.IsPublished,
                bankCountByTopic.GetValueOrDefault(t.Id),
                t.CreatedAt,
                t.UpdatedAt))
            .ToList();
    }
}
