using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Stats;
using TrainerService.Core.Database;
using TrainerService.Domain.Snapshots;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetTrendsQuery(Guid UserId) : IQuery;

public sealed class GetTrendsEndpoint : IEndpoint
{
    /// <summary>На сколько дней назад берётся «было» в сравнении «vs месяц назад».</summary>
    public const int COMPARISON_DAYS = 30;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/stats/trends",
                async Task<EndpointResult<TrainerTrendsDto>> (
                    GetTrendsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetTrendsQuery(user.UserId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Тренд mastery вызывающего «vs месяц назад» (#681 T6): для каждой темы сравнивает сегодняшний снимок
///     mastery со снимком ~<see cref="GetTrendsEndpoint.COMPARISON_DAYS"/>-дневной давности (оба — из
///     <c>topic_mastery_snapshots</c> через <see cref="IStatSnapshotRepository.GetUserTopicMasteryOnDatesAsync"/>).
///     Темы среза — те, у кого есть СЕГОДНЯШНИЙ снимок (якорь «now»); историческое «then» = null, если снимка
///     месяц назад нет (ранний пользователь). Overall-поля — только по «сравнимым» темам (есть оба снимка),
///     чтобы now − then = delta точно. Own-data (scoped по UserId). Заголовки тем — отдельным batched-чтением,
///     fallback «Тема» для удалённых.
/// </summary>
public sealed class GetTrendsHandler : IQueryHandlerWithResult<TrainerTrendsDto, GetTrendsQuery>
{
    private readonly IStatSnapshotRepository _snapshots;
    private readonly ITopicsRepository _topics;

    public GetTrendsHandler(IStatSnapshotRepository snapshots, ITopicsRepository topics)
    {
        _snapshots = snapshots;
        _topics = topics;
    }

    public async Task<Result<TrainerTrendsDto, Error>> Handle(
        GetTrendsQuery query,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly monthAgo = today.AddDays(-GetTrendsEndpoint.COMPARISON_DAYS);

        IReadOnlyList<TopicMasterySnapshot> snapshots =
            await _snapshots.GetUserTopicMasteryOnDatesAsync(query.UserId, today, monthAgo, cancellationToken);

        // Split the two-date result by snapshot date; "now" is the today snapshot, "then" is the month-ago one.
        Dictionary<Guid, double> nowByTopic = snapshots
            .Where(s => s.SnapshotDate == today)
            .ToDictionary(s => s.TopicId, s => s.Mastery);
        Dictionary<Guid, double> thenByTopic = snapshots
            .Where(s => s.SnapshotDate == monthAgo)
            .ToDictionary(s => s.TopicId, s => s.Mastery);

        // No "now" anchor (the daily job hasn't produced today's snapshot, or the user has no mastery yet)
        // → nothing to trend against. Graceful empty rather than a half-populated comparison.
        if (nowByTopic.Count == 0)
            return new TrainerTrendsDto(GetTrendsEndpoint.COMPARISON_DAYS, [], null, null, null);

        var topicIds = nowByTopic.Keys.ToList();
        IReadOnlyList<Topic> topics =
            await _topics.GetManyByAsync(t => topicIds.Contains(t.Id), cancellationToken);
        Dictionary<Guid, string> titleByTopic = topics.ToDictionary(t => t.Id, t => t.Title);

        List<TopicMasteryTrendDto> rows = nowByTopic
            .Select(kv =>
            {
                int masteryNow = RoundPercent(kv.Value);
                int? masteryThen = thenByTopic.TryGetValue(kv.Key, out double then) ? RoundPercent(then) : null;
                int? delta = masteryThen is null ? null : masteryNow - masteryThen.Value;
                return new TopicMasteryTrendDto(
                    kv.Key, titleByTopic.GetValueOrDefault(kv.Key, "Тема"), masteryNow, masteryThen, delta);
            })
            // Topics with history first (so the comparable ones lead), biggest improvement first,
            // then stable title order for deterministic output (and tests).
            .OrderByDescending(r => r.Delta.HasValue)
            .ThenByDescending(r => r.Delta ?? 0)
            .ThenBy(r => r.TopicTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Overall over the COMPARABLE set (topics with both snapshots) so now − then == delta exactly.
        var comparable = rows.Where(r => r.MasteryThen.HasValue).ToList();
        int? overallNow = null, overallThen = null, overallDelta = null;
        if (comparable.Count > 0)
        {
            overallNow = (int)Math.Round(comparable.Average(r => r.MasteryNow), MidpointRounding.AwayFromZero);
            overallThen = (int)Math.Round(comparable.Average(r => r.MasteryThen!.Value), MidpointRounding.AwayFromZero);
            overallDelta = overallNow - overallThen;
        }

        return new TrainerTrendsDto(GetTrendsEndpoint.COMPARISON_DAYS, rows, overallNow, overallThen, overallDelta);
    }

    // Snapshot mastery is stored as double (copied from the integer mastery_percent) — round to a clean percent.
    private static int RoundPercent(double mastery) => (int)Math.Round(mastery, MidpointRounding.AwayFromZero);
}
