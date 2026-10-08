using System.Data.Common;
using AccessService.Contracts.Plans.Dtos;
using AccessService.Core.Database;
using AccessService.Domain;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class GetPlanStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/plans/{planId:guid}/stats/", async Task<EndpointResult<PlanStatsDto>> (
                [FromRoute] Guid planId,
                [FromQuery] int? periodDays,
                [FromServices] GetPlanStatsHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetPlanStatsQuery(planId, periodDays), ct))
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetPlanStatsQuery(Guid PlanId, int? PeriodDays) : IQuery;

public sealed class GetPlanStatsHandler : IQueryHandlerWithResult<PlanStatsDto, GetPlanStatsQuery>
{
    private const int DEFAULT_PERIOD_DAYS = 30;
    private const int MAX_PERIOD_DAYS = 365;
    private const int MIN_PERIOD_DAYS = 1;

    private readonly IPlansRepository _plans;
    private readonly ITransactionManager _transactions;
    private readonly UserScopedData _user;
    private readonly TimeProvider _time;

    public GetPlanStatsHandler(
        IPlansRepository plans,
        ITransactionManager transactions,
        UserScopedData user,
        TimeProvider time)
    {
        _plans = plans;
        _transactions = transactions;
        _user = user;
        _time = time;
    }

    public async Task<Result<PlanStatsDto, Error>> Handle(
        GetPlanStatsQuery query,
        CancellationToken cancellationToken = default)
    {
        Result<Plan, Error> getPlan = await _plans.GetByAsync(
            p => p.Id == query.PlanId, cancellationToken);
        if (getPlan.IsFailure)
        {
            return getPlan.Error;
        }

        Plan plan = getPlan.Value;
        if (!_user.IsOwnerOrAdmin(plan.AuthorId))
        {
            return AccessErrors.AccessDenied();
        }

        int periodDays = NormalizePeriod(query.PeriodDays);
        DateTimeOffset nowUtc = _time.GetUtcNow();
        DateTimeOffset cutoffUtc = nowUtc.AddDays(-periodDays);

        DbConnection connection = _transactions.GetDbConnection();

        const string countsSql = """
            SELECT
                COUNT(*) FILTER (WHERE status = 'ACTIVE') AS active,
                COUNT(*) FILTER (WHERE status = 'REVOKED') AS revoked,
                COUNT(*) FILTER (WHERE status = 'EXPIRED') AS expired,
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE granted_at >= @Now - interval '7 days') AS last_7,
                COUNT(*) FILTER (WHERE granted_at >= @Now - interval '30 days') AS last_30,
                COUNT(*) FILTER (WHERE granted_at >= @Now - interval '90 days') AS last_90
            FROM plan_grants
            WHERE plan_id = @PlanId;
            """;

        const string sourceBreakdownSql = """
            SELECT source AS Source, COUNT(*) AS Count
            FROM plan_grants
            WHERE plan_id = @PlanId
            GROUP BY source
            ORDER BY Count DESC, source ASC;
            """;

        const string timeseriesSql = """
            SELECT
                (granted_at AT TIME ZONE 'UTC')::date AS Day,
                source AS Source,
                COUNT(*) AS Count
            FROM plan_grants
            WHERE plan_id = @PlanId
              AND granted_at >= @Cutoff
            GROUP BY Day, source
            ORDER BY Day;
            """;

        const string inviteLinksSql = """
            SELECT
                il.id              AS InviteLinkId,
                il.label           AS Label,
                il.token           AS Token,
                il.is_active       AS IsActive,
                il.created_at      AS CreatedAt,
                COUNT(r.id)        AS ActivationsCount,
                COUNT(DISTINCT r.user_id) AS UniqueGrantsCount,
                MIN(r.redeemed_at) AS FirstActivationAt,
                MAX(r.redeemed_at) AS LastActivationAt
            FROM invite_links il
            LEFT JOIN invite_redemptions r ON r.invite_link_id = il.id
            WHERE il.plan_id = @PlanId
            GROUP BY il.id, il.label, il.token, il.is_active, il.created_at
            ORDER BY ActivationsCount DESC, il.created_at DESC;
            """;

        object args = new
        {
            PlanId = plan.Id,
            Now = nowUtc,
            Cutoff = cutoffUtc,
        };

        CountsRow counts = await connection.QuerySingleAsync<CountsRow>(
            new CommandDefinition(countsSql, args, cancellationToken: cancellationToken));

        IReadOnlyList<SourceCountRow> sourceRows =
            (await connection.QueryAsync<SourceCountRow>(
                new CommandDefinition(sourceBreakdownSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<TimeseriesRow> timeseriesRows =
            (await connection.QueryAsync<TimeseriesRow>(
                new CommandDefinition(timeseriesSql, args, cancellationToken: cancellationToken))).ToList();

        IReadOnlyList<InviteLinkRow> inviteRows =
            (await connection.QueryAsync<InviteLinkRow>(
                new CommandDefinition(inviteLinksSql, args, cancellationToken: cancellationToken))).ToList();

        return new PlanStatsDto(
            new PlanGrantTotalsDto(counts.Active, counts.Revoked, counts.Expired, counts.Total),
            new PlanGrantPeriodCountersDto(counts.Last_7, counts.Last_30, counts.Last_90),
            sourceRows.Select(r => new PlanGrantSourceBreakdownDto(r.Source, r.Count)).ToList(),
            BuildTimeseries(timeseriesRows, cutoffUtc, nowUtc),
            inviteRows.Select(r => new PlanInviteLinkStatsDto(
                    r.InviteLinkId,
                    r.Label,
                    r.Token,
                    r.IsActive,
                    r.ActivationsCount,
                    r.UniqueGrantsCount,
                    r.FirstActivationAt,
                    r.LastActivationAt))
                .ToList());
    }

    private static IReadOnlyList<PlanStatsTimeseriesPointDto> BuildTimeseries(
        IReadOnlyList<TimeseriesRow> rows,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc)
    {
        // Dense fill: гарантируем точку на каждый день периода, даже если grants
        // не выдавались — иначе recharts рисует «пропуски» и тренд не читается.
        DateOnly startDay = DateOnly.FromDateTime(cutoffUtc.UtcDateTime.Date);
        DateOnly endDay = DateOnly.FromDateTime(nowUtc.UtcDateTime.Date);

        Dictionary<DateOnly, Dictionary<string, long>> bucketsByDay = new();
        foreach (TimeseriesRow row in rows)
        {
            if (!bucketsByDay.TryGetValue(row.Day, out Dictionary<string, long>? bySource))
            {
                bySource = new Dictionary<string, long>(StringComparer.Ordinal);
                bucketsByDay[row.Day] = bySource;
            }
            bySource[row.Source] = row.Count;
        }

        List<PlanStatsTimeseriesPointDto> points = new();
        for (DateOnly d = startDay; d <= endDay; d = d.AddDays(1))
        {
            Dictionary<string, long> bySource = bucketsByDay.TryGetValue(d, out Dictionary<string, long>? b)
                ? b
                : new Dictionary<string, long>(StringComparer.Ordinal);
            long dayTotal = 0;
            foreach (long v in bySource.Values) dayTotal += v;
            points.Add(new PlanStatsTimeseriesPointDto(d, dayTotal, bySource));
        }
        return points;
    }

    private static int NormalizePeriod(int? requested)
    {
        if (requested is null) return DEFAULT_PERIOD_DAYS;
        if (requested.Value < MIN_PERIOD_DAYS) return MIN_PERIOD_DAYS;
        if (requested.Value > MAX_PERIOD_DAYS) return MAX_PERIOD_DAYS;
        return requested.Value;
    }

    private sealed record CountsRow
    {
        public long Active { get; init; }
        public long Revoked { get; init; }
        public long Expired { get; init; }
        public long Total { get; init; }
        public long Last_7 { get; init; }
        public long Last_30 { get; init; }
        public long Last_90 { get; init; }
    }

    private sealed record SourceCountRow(string Source, long Count);

    private sealed record TimeseriesRow(DateOnly Day, string Source, long Count);

    private sealed record InviteLinkRow
    {
        public Guid InviteLinkId { get; init; }
        public string? Label { get; init; }
        public string Token { get; init; } = "";
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public long ActivationsCount { get; init; }
        public long UniqueGrantsCount { get; init; }
        public DateTimeOffset? FirstActivationAt { get; init; }
        public DateTimeOffset? LastActivationAt { get; init; }
    }
}
