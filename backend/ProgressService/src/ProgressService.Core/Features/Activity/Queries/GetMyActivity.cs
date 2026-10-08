using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Gamification;

namespace ProgressService.Core.Features.Activity.Queries;

/// <summary>
/// Запрос на получение сводки активности текущего пользователя:
/// XP и изученные материалы по дням, стрик и итоги за всё время.
/// </summary>
public sealed record GetMyActivityQuery : IQuery;

/// <summary>
/// Публикует endpoint чтения активности текущего пользователя для страницы «Мой прогресс».
/// </summary>
public sealed class GetMyActivityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/my/activity",
                async Task<EndpointResult<MyActivityResponse>> (
                        GetMyActivityHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyActivityQuery(), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

/// <summary>
/// Считает дневную активность пользователя за последние 84 дня (12 недель),
/// стрик по всей истории и суммарные итоги. Все даты — в UTC (MVP, без
/// пользовательских таймзон).
/// </summary>
public sealed class GetMyActivityHandler
    : IQueryHandlerWithResult<MyActivityResponse, GetMyActivityQuery>
{
    private const int WINDOW_DAYS = 84;

    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public GetMyActivityHandler(
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _user = user;
    }

    /// <summary>
    /// Читает агрегаты активности одним round-trip'ом (паттерн
    /// <c>GetRoadmapProgress</c>, issue #217): XP по дням окна, изученные
    /// материалы по дням окна, итоги за всё время и полный список дней
    /// активности для стрика.
    /// </summary>
    public async Task<Result<MyActivityResponse, Error>> Handle(
        GetMyActivityQuery query,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly windowStartDate = today.AddDays(-(WINDOW_DAYS - 1));
        DateTime windowStart = new(windowStartDate, TimeOnly.MinValue, DateTimeKind.Utc);

        DbConnection connection = _transactionManager.GetDbConnection();

        // Дни считаются через AT TIME ZONE 'UTC' — результат не зависит от
        // TimeZone-настройки PG-сессии. День активности = день с любым из:
        // XP-начислением (created_at), визитом материала (viewed_at) или
        // отметкой «изучено» (completed_at). Последний SELECT отдаёт всю
        // историю — из неё считаются current и longest стрики.
        const string sql = """
                           SELECT (xa.created_at AT TIME ZONE 'UTC')::date AS Day,
                                  CAST(SUM(xa.xp_amount) AS int) AS Value
                           FROM xp_awards xa
                           WHERE xa.user_id = @UserId AND xa.created_at >= @WindowStart
                           GROUP BY 1;

                           SELECT (mv.completed_at AT TIME ZONE 'UTC')::date AS Day,
                                  CAST(COUNT(*) AS int) AS Value
                           FROM material_views mv
                           WHERE mv.user_id = @UserId
                             AND mv.is_completed = TRUE
                             AND mv.completed_at >= @WindowStart
                           GROUP BY 1;

                           SELECT
                               CAST(COALESCE((SELECT SUM(xp_amount) FROM xp_awards WHERE user_id = @UserId), 0) AS int) AS TotalXp,
                               CAST((SELECT COUNT(*) FROM material_views WHERE user_id = @UserId AND is_completed = TRUE) AS int) AS MaterialsCompleted,
                               CAST((SELECT COUNT(*) FROM xp_awards WHERE user_id = @UserId AND award_type = @IssueApprovedAwardType) AS int) AS IssuesApproved;

                           SELECT activity.Day
                           FROM (
                               SELECT (created_at AT TIME ZONE 'UTC')::date AS Day
                               FROM xp_awards
                               WHERE user_id = @UserId
                               UNION
                               SELECT (viewed_at AT TIME ZONE 'UTC')::date
                               FROM material_views
                               WHERE user_id = @UserId
                               UNION
                               SELECT (completed_at AT TIME ZONE 'UTC')::date
                               FROM material_views
                               WHERE user_id = @UserId AND completed_at IS NOT NULL
                           ) AS activity
                           ORDER BY activity.Day;
                           """;

        var parameters = new
        {
            UserId = _user.UserId,
            WindowStart = windowStart,
            IssueApprovedAwardType = nameof(XpAwardType.ISSUE_APPROVED),
        };

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        Dictionary<DateOnly, int> xpByDay = (await multi.ReadAsync<DayValueRow>())
            .ToDictionary(r => r.Day, r => r.Value);
        Dictionary<DateOnly, int> materialsByDay = (await multi.ReadAsync<DayValueRow>())
            .ToDictionary(r => r.Day, r => r.Value);
        TotalsRow totals = await multi.ReadFirstAsync<TotalsRow>();
        List<DateOnly> activityDays = (await multi.ReadAsync<ActivityDayRow>())
            .Select(r => r.Day)
            .ToList();

        var days = new List<ActivityDayDto>(WINDOW_DAYS);
        for (int offset = WINDOW_DAYS - 1; offset >= 0; offset--)
        {
            DateOnly day = today.AddDays(-offset);
            days.Add(new ActivityDayDto(
                day,
                xpByDay.GetValueOrDefault(day),
                materialsByDay.GetValueOrDefault(day)));
        }

        (int current, int longest) = ComputeStreaks(activityDays, today);

        return new MyActivityResponse(
            days,
            new ActivityStreakDto(current, longest),
            new ActivityTotalsDto(totals.TotalXp, totals.MaterialsCompleted, totals.IssuesApproved));
    }

    /// <summary>
    /// Считает стрики по отсортированному списку уникальных дней активности.
    /// <c>longest</c> — лучшая серия за всю историю. <c>current</c> — серия,
    /// заканчивающаяся сегодня ИЛИ вчера: отсутствие активности сегодня не
    /// рвёт серию до завтра (grace-day, стандартная streak-семантика).
    /// </summary>
    private static (int Current, int Longest) ComputeStreaks(
        IReadOnlyList<DateOnly> activityDays,
        DateOnly today)
    {
        int longest = 0;
        int run = 0;
        DateOnly previous = default;

        foreach (DateOnly day in activityDays)
        {
            run = run > 0 && day == previous.AddDays(1) ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }

        int current = 0;
        if (activityDays.Count > 0)
        {
            DateOnly last = activityDays[^1];
            if (last == today || last == today.AddDays(-1))
            {
                current = 1;
                for (int i = activityDays.Count - 2; i >= 0; i--)
                {
                    if (activityDays[i].AddDays(1) != activityDays[i + 1])
                    {
                        break;
                    }

                    current++;
                }
            }
        }

        return (current, longest);
    }

    /// <summary>
    /// Проекция строки «день → значение» (XP или количество материалов).
    /// </summary>
    private sealed class DayValueRow
    {
        public DateOnly Day { get; init; }

        public int Value { get; init; }
    }

    /// <summary>
    /// Проекция строки суммарных итогов.
    /// </summary>
    private sealed class TotalsRow
    {
        public int TotalXp { get; init; }

        public int MaterialsCompleted { get; init; }

        public int IssuesApproved { get; init; }
    }

    /// <summary>
    /// Проекция строки дня активности.
    /// </summary>
    private sealed class ActivityDayRow
    {
        public DateOnly Day { get; init; }
    }
}
