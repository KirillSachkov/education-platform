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
using TrainerService.Core.Features.Stats.UserLookup;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Stats.Queries;

public sealed record GetAdminStatsQuery(int Days) : IQuery;

public sealed class GetAdminStatsEndpoint : IEndpoint
{
    public const int DEFAULT_DAYS = 30;
    public const int MIN_DAYS = 1;
    public const int MAX_DAYS = 365;

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/trainer/admin/stats",
                async Task<EndpointResult<AdminStatsDto>> (
                    GetAdminStatsHandler handler,
                    CancellationToken cancellationToken,
                    int? days = null) =>
                    await handler.Handle(
                        new GetAdminStatsQuery(Math.Clamp(days ?? DEFAULT_DAYS, MIN_DAYS, MAX_DAYS)),
                        cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN)
            .RequireRateLimiting("admin-stats");
    }
}

/// <summary>
///     Admin-дашборд тренажёра (#614 D1): AI-расходы (из лоджера <c>trainer.ai_usage</c>, поверх C-ledger'а)
///     + использование (из <c>trainer.training_sessions</c>) за окно <c>days</c> (clamp 1..365, default 30,
///     <c>created_at/started_at &gt;= now - days</c>). Dapper-агрегаты (raw SQL GROUP BY), без EF-загрузки
///     сущностей — зеркалит AccessService <c>GetPlanStats</c>. Дневной ряд стоимости плотный (zero-filled
///     в C#, как densify в AccessService). Только AI-СТОИМОСТЬ + активность; выручка/маржа — на стороне
///     AccessService, не здесь.
/// </summary>
public sealed class GetAdminStatsHandler : IQueryHandlerWithResult<AdminStatsDto, GetAdminStatsQuery>
{
    // Modes always returned in this order even with zero data — mirrors the difficulty-bucket convention.
    private static readonly string[] ModeOrder = ["DRILL", "LEARN", "MOCK"];

    private const int TOP_USERS_LIMIT = 10;

    private readonly ITransactionManager _transactions;
    private readonly IUserLookupClient _userLookup;
    private readonly ILogger<GetAdminStatsHandler> _logger;

    public GetAdminStatsHandler(
        ITransactionManager transactions,
        IUserLookupClient userLookup,
        ILogger<GetAdminStatsHandler> logger)
    {
        _transactions = transactions;
        _userLookup = userLookup;
        _logger = logger;
    }

    public async Task<Result<AdminStatsDto, Error>> Handle(
        GetAdminStatsQuery query,
        CancellationToken cancellationToken)
    {
        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        DateTimeOffset cutoffUtc = nowUtc.AddDays(-query.Days);

        DbConnection connection = _transactions.GetDbConnection();

        object args = new { Cutoff = cutoffUtc, TopLimit = TOP_USERS_LIMIT };

        // --- AI spend ---

        // SUM over a bigint column returns numeric in Postgres → cast back to bigint so Dapper maps to long.
        // SUM over int (tokens) returns bigint already; COALESCE casts cover the all-null group.
        const string spendTotalsSql = """
            SELECT
                COALESCE(SUM(cost_micro_rub), 0)::bigint    AS TotalCostMicroRub,
                COUNT(*)                                    AS TotalOperations,
                COUNT(DISTINCT user_id)                     AS DistinctUsers,
                COALESCE(SUM(input_tokens), 0)::bigint      AS TotalInputTokens,
                COALESCE(SUM(output_tokens), 0)::bigint     AS TotalOutputTokens
            FROM ai_usage
            WHERE created_at >= @Cutoff;
            """;

        const string byOperationSql = """
            SELECT
                operation                                AS Operation,
                COUNT(*)                                 AS Count,
                COALESCE(SUM(cost_micro_rub), 0)::bigint  AS CostMicroRub
            FROM ai_usage
            WHERE created_at >= @Cutoff
            GROUP BY operation
            ORDER BY CostMicroRub DESC, operation ASC;
            """;

        const string byModelSql = """
            SELECT
                model                                    AS Model,
                COUNT(*)                                 AS Count,
                COALESCE(SUM(cost_micro_rub), 0)::bigint  AS CostMicroRub,
                COALESCE(SUM(input_tokens), 0)::bigint    AS InputTokens,
                COALESCE(SUM(output_tokens), 0)::bigint   AS OutputTokens
            FROM ai_usage
            WHERE created_at >= @Cutoff
            GROUP BY model
            ORDER BY CostMicroRub DESC, model ASC;
            """;

        const string dailySql = """
            SELECT
                (date_trunc('day', created_at AT TIME ZONE 'UTC'))::date AS Day,
                COALESCE(SUM(cost_micro_rub), 0)::bigint                  AS CostMicroRub
            FROM ai_usage
            WHERE created_at >= @Cutoff
            GROUP BY Day
            ORDER BY Day;
            """;

        const string topUsersSql = """
            SELECT
                user_id                                  AS UserId,
                COALESCE(SUM(cost_micro_rub), 0)::bigint  AS CostMicroRub,
                COUNT(*)                                 AS OperationCount
            FROM ai_usage
            WHERE created_at >= @Cutoff
            GROUP BY user_id
            ORDER BY CostMicroRub DESC, user_id ASC
            LIMIT @TopLimit;
            """;

        // --- Usage ---

        const string sessionsTotalsSql = """
            SELECT
                COUNT(*)                                          AS SessionsStarted,
                COUNT(DISTINCT user_id)                           AS ActiveUsers,
                COUNT(*) FILTER (WHERE status = 'COMPLETED')      AS CompletedSessions
            FROM training_sessions
            WHERE started_at >= @Cutoff;
            """;

        const string byModeSql = """
            SELECT mode AS Mode, COUNT(*) AS Count
            FROM training_sessions
            WHERE started_at >= @Cutoff
            GROUP BY mode;
            """;

        SpendTotalsRow spendTotals = await connection.QuerySingleAsync<SpendTotalsRow>(
            new CommandDefinition(spendTotalsSql, args, cancellationToken: cancellationToken));

        IReadOnlyList<OperationRow> operationRows =
            (await connection.QueryAsync<OperationRow>(
                new CommandDefinition(byOperationSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<ModelRow> modelRows =
            (await connection.QueryAsync<ModelRow>(
                new CommandDefinition(byModelSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<DailyRow> dailyRows =
            (await connection.QueryAsync<DailyRow>(
                new CommandDefinition(dailySql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<TopUserRow> topUserRows =
            (await connection.QueryAsync<TopUserRow>(
                new CommandDefinition(topUsersSql, args, cancellationToken: cancellationToken))).ToList();

        SessionTotalsRow sessionTotals = await connection.QuerySingleAsync<SessionTotalsRow>(
            new CommandDefinition(sessionsTotalsSql, args, cancellationToken: cancellationToken));

        IReadOnlyList<ModeRow> modeRows =
            (await connection.QueryAsync<ModeRow>(
                new CommandDefinition(byModeSql, args, cancellationToken: cancellationToken))).ToList();

        // Top-user enrichment (#681): batch-resolve display name + avatar from AuthService. Best-effort —
        // a failure (or the lookup throwing) degrades to ids only, never a 500.
        IReadOnlyDictionary<Guid, UserLookupDto> topUserCredit =
            await ResolveTopUserCreditAsync(topUserRows, cancellationToken);

        // OPEN_ANSWER_GRADE op count for cost-per-grade (absent operation → 0 → metric 0).
        long gradeOperations = operationRows
            .FirstOrDefault(r => string.Equals(
                r.Operation, nameof(AiUsageOperation.OPEN_ANSWER_GRADE), StringComparison.Ordinal))
            ?.Count ?? 0L;

        AdminAiMoneyMetricsDto money = BuildMoney(
            spendTotals.TotalCostMicroRub,
            spendTotals.DistinctUsers,
            sessionTotals.SessionsStarted,
            gradeOperations,
            query.Days);

        AdminAiSpendDto aiSpend = new(
            spendTotals.TotalCostMicroRub,
            spendTotals.TotalCostMicroRub / 1_000_000m,
            spendTotals.TotalOperations,
            spendTotals.TotalInputTokens,
            spendTotals.TotalOutputTokens,
            operationRows.Select(r => new AdminAiOperationBreakdownDto(r.Operation, r.Count, r.CostMicroRub)).ToList(),
            modelRows.Select(r => new AdminAiModelBreakdownDto(
                    r.Model, r.Count, r.CostMicroRub, r.InputTokens, r.OutputTokens))
                .ToList(),
            BuildDenseDaily(dailyRows, cutoffUtc, nowUtc),
            topUserRows.Select(r =>
            {
                topUserCredit.TryGetValue(r.UserId, out UserLookupDto? credit);
                return new AdminAiTopUserDto(
                    r.UserId, r.CostMicroRub, r.OperationCount, credit?.DisplayName, credit?.AvatarUrl);
            }).ToList(),
            money);

        AdminUsageDto usage = new(
            sessionTotals.SessionsStarted,
            BuildModeBreakdown(modeRows),
            sessionTotals.ActiveUsers,
            sessionTotals.CompletedSessions);

        // margin = revenue (AccessService plan-stats) − this cost; D2 links there.
        return new AdminStatsDto(query.Days, aiSpend, usage);
    }

    /// <summary>
    ///     Плотный дневной ряд: точка на каждый день периода, даже если в этот день не было AI-вызовов
    ///     (иначе recharts рисует «пропуски» и тренд не читается) — зеркалит densify в AccessService stats.
    /// </summary>
    private static IReadOnlyList<AdminAiDailyPointDto> BuildDenseDaily(
        IReadOnlyList<DailyRow> rows,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc)
    {
        Dictionary<DateOnly, long> costByDay = rows.ToDictionary(r => r.Day, r => r.CostMicroRub);

        DateOnly startDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime.Date);
        DateOnly endDay = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);

        List<AdminAiDailyPointDto> points = new();
        for (DateOnly d = startDay; d <= endDay; d = d.AddDays(1))
        {
            points.Add(new AdminAiDailyPointDto(d, costByDay.GetValueOrDefault(d, 0L)));
        }

        return points;
    }

    /// <summary>Все три режима (DRILL/LEARN/MOCK) всегда возвращаются, нулями при отсутствии данных.</summary>
    private static IReadOnlyList<AdminUsageModeBreakdownDto> BuildModeBreakdown(IReadOnlyList<ModeRow> rows)
    {
        Dictionary<string, long> countByMode = rows.ToDictionary(r => r.Mode, r => r.Count, StringComparer.Ordinal);
        return ModeOrder
            .Select(mode => new AdminUsageModeBreakdownDto(mode, countByMode.GetValueOrDefault(mode, 0L)))
            .ToList();
    }

    /// <summary>
    ///     Best-effort batch resolve of top-spender display name + avatar from AuthService. The client
    ///     soft-degrades to an empty dict on outage; this extra try/catch ensures even an unexpected throw
    ///     leaves the admin stats endpoint at 200 (ids without names) rather than failing.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, UserLookupDto>> ResolveTopUserCreditAsync(
        IReadOnlyList<TopUserRow> topUserRows,
        CancellationToken ct)
    {
        if (topUserRows.Count == 0)
            return new Dictionary<Guid, UserLookupDto>();

        IReadOnlyList<Guid> ids = topUserRows.Select(r => r.UserId).ToList();
        try
        {
            return await _userLookup.GetUsersAsync(ids, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Top-user credit lookup failed for {Count} ids; returning ids only.", ids.Count);
            return new Dictionary<Guid, UserLookupDto>();
        }
    }

    /// <summary>
    ///     Derived "money" metrics over the window (#681): cost per distinct user, per started session,
    ///     per OPEN_ANSWER_GRADE op, and a 30-day projection (window daily-average × 30). Each in
    ///     micro-rub (source of truth) + rub (display). A zero denominator yields 0 (no divide).
    /// </summary>
    private static AdminAiMoneyMetricsDto BuildMoney(
        long totalCostMicroRub,
        long distinctUsers,
        long sessionsStarted,
        long gradeOperations,
        int days)
    {
        long perUser = Average(totalCostMicroRub, distinctUsers);
        long perSession = Average(totalCostMicroRub, sessionsStarted);
        long perGrade = Average(totalCostMicroRub, gradeOperations);

        // days is clamped to >= 1 by the endpoint, so this never divides by zero.
        long projectedMonth = (long)Math.Round(
            (decimal)totalCostMicroRub / days * 30m, MidpointRounding.AwayFromZero);

        return new AdminAiMoneyMetricsDto(
            perUser, MicroToRub(perUser),
            perSession, MicroToRub(perSession),
            perGrade, MicroToRub(perGrade),
            projectedMonth, MicroToRub(projectedMonth));
    }

    private static long Average(long totalMicroRub, long denominator)
        => denominator <= 0
            ? 0L
            : (long)Math.Round((decimal)totalMicroRub / denominator, MidpointRounding.AwayFromZero);

    private static decimal MicroToRub(long microRub) => microRub / 1_000_000m;

    private sealed record SpendTotalsRow
    {
        public long TotalCostMicroRub { get; init; }
        public long TotalOperations { get; init; }
        public long DistinctUsers { get; init; }
        public long TotalInputTokens { get; init; }
        public long TotalOutputTokens { get; init; }
    }

    private sealed record OperationRow(string Operation, long Count, long CostMicroRub);

    private sealed record ModelRow(string Model, long Count, long CostMicroRub, long InputTokens, long OutputTokens);

    private sealed record DailyRow(DateOnly Day, long CostMicroRub);

    private sealed record TopUserRow(Guid UserId, long CostMicroRub, long OperationCount);

    private sealed record SessionTotalsRow
    {
        public long SessionsStarted { get; init; }
        public long ActiveUsers { get; init; }
        public long CompletedSessions { get; init; }
    }

    private sealed record ModeRow(string Mode, long Count);
}
