using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using TrainerService.Contracts.Stats;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetActivityStatsQuery(Guid UserId, int Days) : IQuery;

public sealed class GetActivityStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 90;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/stats/activity",
                async Task<EndpointResult<TrainerActivityDto>> (
                    GetActivityStatsHandler handler,
                    UserScopedData user,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetActivityStatsQuery(user.UserId, Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
///     Временная ось активности тренажёра вызывающего (#568): по дням за окно <c>days</c> — сколько
///     сессий начато + answered/correct из авто-грейдимых item'ов (с баллом). Streak'и: current —
///     дни подряд с ≥1 сессией до сегодня (UTC); longest — самая длинная серия за всё время (вне окна).
///     Только собственные данные (scoped по UserId).
/// </summary>
public sealed class GetActivityStatsHandler : IQueryHandlerWithResult<TrainerActivityDto, GetActivityStatsQuery>
{
    private readonly ITrainingSessionsRepository _sessions;

    public GetActivityStatsHandler(ITrainingSessionsRepository sessions) => _sessions = sessions;

    public async Task<Result<TrainerActivityDto, Error>> Handle(
        GetActivityStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTime today = DateTime.UtcNow.Date;
        DateTime since = today.AddDays(-(query.Days - 1));

        IReadOnlyList<TrainingSession> windowed =
            await _sessions.GetWithItemsStartedSinceAsync(query.UserId, since, cancellationToken);

        var days = windowed
            .GroupBy(s => DateOnly.FromDateTime(s.StartedAt.Date))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var items = g.SelectMany(s => s.Items).ToList();
                int answered = items.Count(i => i.ScorePercent.HasValue);
                int correct = items.Count(i => i.Verdict == AnswerVerdict.CORRECT);
                return new TrainerActivityDayDto(
                    g.Key,
                    g.Count(),
                    answered,
                    correct,
                    answered == 0 ? 0 : (int)Math.Round(correct * 100.0 / answered, MidpointRounding.AwayFromZero));
            })
            .ToList();

        IReadOnlyList<DateOnly> activeDates =
            await _sessions.GetDistinctSessionDatesAsync(query.UserId, cancellationToken);

        DateOnly todayDate = DateOnly.FromDateTime(today);
        int currentStreak = ComputeCurrentStreak(activeDates, todayDate);
        int longestStreak = ComputeLongestStreak(activeDates);

        return new TrainerActivityDto(days, currentStreak, longestStreak);
    }

    /// <summary>
    ///     Дни подряд с активностью до сегодня. Серия «живая», если последний активный день — сегодня
    ///     ИЛИ вчера (сегодня ещё могло не быть тренировки). <paramref name="activeDates"/> — newest-first.
    /// </summary>
    private static int ComputeCurrentStreak(IReadOnlyList<DateOnly> activeDates, DateOnly today)
    {
        if (activeDates.Count == 0)
            return 0;

        DateOnly mostRecent = activeDates[0];
        if (mostRecent != today && mostRecent != today.AddDays(-1))
            return 0; // серия прервана: последняя тренировка раньше вчера.

        int streak = 1;
        DateOnly cursor = mostRecent;
        for (int i = 1; i < activeDates.Count; i++)
        {
            DateOnly expected = cursor.AddDays(-1);
            if (activeDates[i] != expected)
                break;

            streak++;
            cursor = activeDates[i];
        }

        return streak;
    }

    /// <summary>Самая длинная серия последовательных активных дней за всё время.</summary>
    private static int ComputeLongestStreak(IReadOnlyList<DateOnly> activeDates)
    {
        if (activeDates.Count == 0)
            return 0;

        // activeDates are distinct + newest-first; walk descending, counting consecutive runs.
        int longest = 1;
        int run = 1;
        for (int i = 1; i < activeDates.Count; i++)
        {
            if (activeDates[i] == activeDates[i - 1].AddDays(-1))
                run++;
            else
                run = 1;

            if (run > longest)
                longest = run;
        }

        return longest;
    }
}
