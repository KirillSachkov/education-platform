using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using TrainerService.Contracts.Admin;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetAdminTrafficStatsQuery(int Days) : IQuery;

public sealed class GetAdminTrafficStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats/traffic",
                async Task<EndpointResult<AdminTrafficStatsDto>> (
                    GetAdminTrafficStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetAdminTrafficStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin-трафик тренажёра (#681 T3): активные пользователи, retention, новые-vs-вернувшиеся и
///     сессии-по-режиму за окно <c>days</c> (clamp 1..365, default 30). Dapper raw-SQL агрегаты над
///     <c>training_sessions</c>, без EF-загрузки — зеркалит <c>GetAdminStats</c>. Дневные ряды плотные
///     (zero-filled в C#, как <c>BuildDenseDaily</c>).
///     <para>
///     <b>Когда какой день UTC:</b> <c>started_at</c> — <c>timestamp without time zone</c>, хранит UTC,
///     поэтому день берётся как <c>started_at::date</c> (без <c>AT TIME ZONE</c> — значение уже UTC).
///     </para>
///     <para>
///     <b>Retention — first-touch когорты.</b> «Первый день» пользователя = MIN(день любой его сессии)
///     по ВСЕЙ таблице (а не только по окну) — так пользователь, активный до окна, не считается новым.
///     Когорта D-N = пользователи, чей первый день попал в окно (<c>fday &gt;= cutoff</c>) И достаточно
///     стар, чтобы N-дневное окно возврата истекло (<c>fday &lt;= today - N</c>). «Вернулся»: D1 — есть
///     сессия РОВНО на <c>fday+1</c>; D7 — любая сессия в <c>(fday, fday+7]</c>; D30 — в <c>(fday, fday+30]</c>.
///     Следствие: при малом <c>days</c> D30-когорта тонкая (нужно ≥N дней истории внутри окна) — это
///     корректно, а не баг.
///     </para>
/// </summary>
public sealed class GetAdminTrafficStatsHandler
    : IQueryHandlerWithResult<AdminTrafficStatsDto, GetAdminTrafficStatsQuery>
{
    // SessionsByMode is broken out as explicit DRILL/LEARN/MOCK columns (see the daily DTO).
    // CHALLENGE is reserved (Ф3, not implemented) and intentionally not surfaced here.
    private const int DAU_DAYS = 1;
    private const int WAU_DAYS = 7;
    private const int MAU_DAYS = 30;

    private readonly ITransactionManager _transactions;

    public GetAdminTrafficStatsHandler(ITransactionManager transactions) => _transactions = transactions;

    public async Task<Result<AdminTrafficStatsDto, Error>> Handle(
        GetAdminTrafficStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset cutoffUtc = nowUtc.AddDays(-query.Days);
        DateOnly todayUtc = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);
        DateOnly cutoffDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime.Date);

        DbConnection connection = _transactions.GetDbConnection();

        // --- Active users: fixed rolling windows, independent of `days`. ---
        const string activeSql = """
            SELECT
                COUNT(DISTINCT user_id) FILTER (WHERE started_at >= @Dau) AS Dau,
                COUNT(DISTINCT user_id) FILTER (WHERE started_at >= @Wau) AS Wau,
                COUNT(DISTINCT user_id) FILTER (WHERE started_at >= @Mau) AS Mau
            FROM training_sessions
            WHERE started_at >= @Mau;
            """;

        ActiveUsersRow active = await connection.QuerySingleAsync<ActiveUsersRow>(
            new CommandDefinition(
                activeSql,
                new
                {
                    Dau = nowUtc.AddDays(-DAU_DAYS),
                    Wau = nowUtc.AddDays(-WAU_DAYS),
                    Mau = nowUtc.AddDays(-MAU_DAYS),
                },
                cancellationToken: cancellationToken));

        // --- Retention: first-touch cohorts (see class summary for the exact definition). ---
        // DAU/WAU/MAU = distinct users active within today−1/−7/−30 UTC calendar days (not a rolling-hours window)
        const string retentionSql = """
            WITH user_days AS (
                SELECT user_id, started_at::date AS day
                FROM training_sessions
                GROUP BY user_id, started_at::date
            ),
            first_day AS (
                SELECT user_id, MIN(day) AS fday
                FROM user_days
                GROUP BY user_id
            ),
            cohort AS (
                SELECT
                    f.user_id,
                    f.fday,
                    bool_or(ud.day = f.fday + 1)                       AS ret_d1,
                    bool_or(ud.day > f.fday AND ud.day <= f.fday + 7)  AS ret_d7,
                    bool_or(ud.day > f.fday AND ud.day <= f.fday + 30) AS ret_d30
                FROM first_day f
                JOIN user_days ud ON ud.user_id = f.user_id
                GROUP BY f.user_id, f.fday
            )
            SELECT
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 1)              AS D1Cohort,
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 1 AND ret_d1)   AS D1Returned,
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 7)              AS D7Cohort,
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 7 AND ret_d7)   AS D7Returned,
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 30)             AS D30Cohort,
                COUNT(*) FILTER (WHERE fday >= @CohortStart::date AND fday <= @Today::date - 30 AND ret_d30) AS D30Returned
            FROM cohort;
            """;

        // DateOnly is passed as timestamp (Unspecified) and cast ::date in SQL — avoids relying on
        // Dapper/Npgsql DateOnly-parameter binding; midnight + ::date is TZ-independent.
        RetentionRow retention = await connection.QuerySingleAsync<RetentionRow>(
            new CommandDefinition(
                retentionSql,
                new { CohortStart = cutoffDay.ToDateTime(TimeOnly.MinValue), Today = todayUtc.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken));

        // --- New-vs-returning per day (distinct users per day, split by first-touch). ---
        const string newReturningSql = """
            WITH user_days AS (
                SELECT user_id, started_at::date AS day
                FROM training_sessions
                GROUP BY user_id, started_at::date
            ),
            first_day AS (
                SELECT user_id, MIN(day) AS fday
                FROM user_days
                GROUP BY user_id
            )
            SELECT
                ud.day                                  AS Day,
                COUNT(*) FILTER (WHERE ud.day = f.fday) AS NewUsers,
                COUNT(*) FILTER (WHERE ud.day > f.fday) AS ReturningUsers
            FROM user_days ud
            JOIN first_day f ON f.user_id = ud.user_id
            WHERE ud.day >= @CutoffDay::date
            GROUP BY ud.day
            ORDER BY ud.day;
            """;

        IReadOnlyList<NewReturningRow> newReturningRows =
            (await connection.QueryAsync<NewReturningRow>(
                new CommandDefinition(
                    newReturningSql,
                    new { CutoffDay = cutoffDay.ToDateTime(TimeOnly.MinValue) },
                    cancellationToken: cancellationToken))).ToList();

        // --- Sessions/day by mode. ---
        const string sessionsByModeSql = """
            SELECT
                started_at::date AS Day,
                mode             AS Mode,
                COUNT(*)         AS Count
            FROM training_sessions
            WHERE started_at >= @Cutoff
            GROUP BY started_at::date, mode
            ORDER BY Day;
            """;

        IReadOnlyList<SessionsByModeRow> sessionsByModeRows =
            (await connection.QueryAsync<SessionsByModeRow>(
                new CommandDefinition(
                    sessionsByModeSql,
                    new { Cutoff = cutoffUtc },
                    cancellationToken: cancellationToken))).ToList();

        DateOnly startDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime.Date);
        DateOnly endDay = todayUtc;

        AdminTrafficStatsDto dto = new(
            query.Days,
            new AdminActiveUsersDto(active.Dau, active.Wau, active.Mau),
            new AdminRetentionDto(
                new AdminRetentionBucketDto(retention.D1Cohort, retention.D1Returned, Rate(retention.D1Returned, retention.D1Cohort)),
                new AdminRetentionBucketDto(retention.D7Cohort, retention.D7Returned, Rate(retention.D7Returned, retention.D7Cohort)),
                new AdminRetentionBucketDto(retention.D30Cohort, retention.D30Returned, Rate(retention.D30Returned, retention.D30Cohort))),
            BuildDenseNewReturning(newReturningRows, startDay, endDay),
            BuildDenseSessionsByMode(sessionsByModeRows, startDay, endDay));

        return dto;
    }

    private static double Rate(long numerator, long denominator) =>
        denominator == 0 ? 0d : (double)numerator / denominator;

    /// <summary>Плотный дневной ряд new-vs-returning: точка на каждый день окна (zero-filled).</summary>
    private static IReadOnlyList<AdminNewReturningDayDto> BuildDenseNewReturning(
        IReadOnlyList<NewReturningRow> rows,
        DateOnly startDay,
        DateOnly endDay)
    {
        Dictionary<DateOnly, NewReturningRow> byDay = rows.ToDictionary(r => r.Day);

        List<AdminNewReturningDayDto> points = new();
        for (DateOnly d = startDay; d <= endDay; d = d.AddDays(1))
        {
            points.Add(byDay.TryGetValue(d, out NewReturningRow? row)
                ? new AdminNewReturningDayDto(d, row.NewUsers, row.ReturningUsers)
                : new AdminNewReturningDayDto(d, 0, 0));
        }

        return points;
    }

    /// <summary>Плотный дневной ряд сессий по режиму: точка на каждый день окна, нули для отсутствующих режимов/дней.</summary>
    private static IReadOnlyList<AdminSessionsByModeDayDto> BuildDenseSessionsByMode(
        IReadOnlyList<SessionsByModeRow> rows,
        DateOnly startDay,
        DateOnly endDay)
    {
        Dictionary<(DateOnly Day, string Mode), long> byDayMode =
            rows.ToDictionary(r => (r.Day, r.Mode), r => r.Count, ModeKeyComparer);

        List<AdminSessionsByModeDayDto> points = new();
        for (DateOnly d = startDay; d <= endDay; d = d.AddDays(1))
        {
            points.Add(new AdminSessionsByModeDayDto(
                d,
                byDayMode.GetValueOrDefault((d, "DRILL"), 0L),
                byDayMode.GetValueOrDefault((d, "LEARN"), 0L),
                byDayMode.GetValueOrDefault((d, "MOCK"), 0L)));
        }

        return points;
    }

    // Ordinal mode comparison so the (day, mode) dictionary keys never depend on culture (MA0006).
    private static readonly IEqualityComparer<(DateOnly Day, string Mode)> ModeKeyComparer =
        new ModeKeyEqualityComparer();

    private sealed class ModeKeyEqualityComparer : IEqualityComparer<(DateOnly Day, string Mode)>
    {
        public bool Equals((DateOnly Day, string Mode) x, (DateOnly Day, string Mode) y) =>
            x.Day == y.Day && string.Equals(x.Mode, y.Mode, StringComparison.Ordinal);

        public int GetHashCode((DateOnly Day, string Mode) obj) =>
            HashCode.Combine(obj.Day, StringComparer.Ordinal.GetHashCode(obj.Mode));
    }

    private sealed record ActiveUsersRow(long Dau, long Wau, long Mau);

    private sealed record RetentionRow(
        long D1Cohort,
        long D1Returned,
        long D7Cohort,
        long D7Returned,
        long D30Cohort,
        long D30Returned);

    private sealed record NewReturningRow(DateOnly Day, long NewUsers, long ReturningUsers);

    private sealed record SessionsByModeRow(DateOnly Day, string Mode, long Count);
}
