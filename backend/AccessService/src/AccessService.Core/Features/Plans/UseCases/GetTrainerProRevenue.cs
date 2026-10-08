using System.Data.Common;
using AccessService.Contracts.Plans.Dtos;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Plans.UseCases;

public sealed class GetTrainerProRevenueEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/access/admin/trainer-pro/revenue/", async Task<EndpointResult<TrainerProRevenueDto>> (
                [FromQuery] int? periodDays,
                [FromServices] GetTrainerProRevenueHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetTrainerProRevenueQuery(periodDays), ct))
            // Платформенный агрегат по всем TRAINER_PRO-планам (единственный автор владеет всеми) —
            // достаточно Plans.MANAGE, как и у per-plan stats; per-plan ownership-чека тут нет.
            .RequirePermissions(PlatformPermissions.Plans.MANAGE);
    }
}

public sealed record GetTrainerProRevenueQuery(int? PeriodDays) : IQuery;

/// <summary>
///     Кросс-плановый агрегат выручки по <c>offer_type = TRAINER_PRO</c> (#623) для админ-дашборда
///     тренажёра. Зеркалит <see cref="GetPlanStatsHandler"/>, но без planId/ownership: считает по всем
///     TRAINER_PRO-планам сразу (платформа одно-авторская). Dapper-агрегаты по <c>plan_grants</c> JOIN
///     <c>plans</c>: активные/отписки/всего + окна 7/30/90д + оценка MRR (сумма price_paid_cents активных)
///     + разбивка по источнику + плотный дневной ряд новых выдач.
/// </summary>
public sealed class GetTrainerProRevenueHandler
    : IQueryHandlerWithResult<TrainerProRevenueDto, GetTrainerProRevenueQuery>
{
    private const string OFFER_TYPE = "TRAINER_PRO";
    private const int DEFAULT_PERIOD_DAYS = 30;
    private const int MAX_PERIOD_DAYS = 365;
    private const int MIN_PERIOD_DAYS = 1;

    private readonly ITransactionManager _transactions;
    private readonly TimeProvider _time;

    public GetTrainerProRevenueHandler(ITransactionManager transactions, TimeProvider time)
    {
        _transactions = transactions;
        _time = time;
    }

    public async Task<Result<TrainerProRevenueDto, Error>> Handle(
        GetTrainerProRevenueQuery query,
        CancellationToken cancellationToken = default)
    {
        int periodDays = NormalizePeriod(query.PeriodDays);
        DateTimeOffset nowUtc = _time.GetUtcNow();
        DateTimeOffset cutoffUtc = nowUtc.AddDays(-periodDays);

        DbConnection connection = _transactions.GetDbConnection();

        const string countsSql = """
            SELECT
                COUNT(*) FILTER (WHERE pg.status = 'ACTIVE') AS active,
                COUNT(*) FILTER (WHERE pg.status = 'ACTIVE' AND COALESCE(pg.price_paid_cents, 0) > 0) AS active_paying,
                COUNT(*) FILTER (WHERE pg.status IN ('REVOKED', 'EXPIRED')) AS canceled,
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE pg.granted_at >= @Now - interval '7 days') AS last_7,
                COUNT(*) FILTER (WHERE pg.granted_at >= @Now - interval '30 days') AS last_30,
                COUNT(*) FILTER (WHERE pg.granted_at >= @Now - interval '90 days') AS last_90,
                COALESCE(SUM(pg.price_paid_cents) FILTER (WHERE pg.status = 'ACTIVE'), 0) AS mrr_cents
            FROM plan_grants pg
            JOIN plans p ON p.id = pg.plan_id
            WHERE p.offer_type = @OfferType;
            """;

        const string sourceBreakdownSql = """
            SELECT pg.source AS Source, COUNT(*) AS Count
            FROM plan_grants pg
            JOIN plans p ON p.id = pg.plan_id
            WHERE p.offer_type = @OfferType
            GROUP BY pg.source
            ORDER BY Count DESC, pg.source ASC;
            """;

        const string timeseriesSql = """
            SELECT
                (pg.granted_at AT TIME ZONE 'UTC')::date AS Day,
                pg.source AS Source,
                COUNT(*) AS Count
            FROM plan_grants pg
            JOIN plans p ON p.id = pg.plan_id
            WHERE p.offer_type = @OfferType
              AND pg.granted_at >= @Cutoff
            GROUP BY Day, pg.source
            ORDER BY Day;
            """;

        object args = new
        {
            OfferType = OFFER_TYPE,
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

        return new TrainerProRevenueDto(
            counts.Active,
            counts.Active_Paying,
            counts.Canceled,
            counts.Total,
            new PlanGrantPeriodCountersDto(counts.Last_7, counts.Last_30, counts.Last_90),
            counts.Mrr_Cents,
            sourceRows.Select(r => new PlanGrantSourceBreakdownDto(r.Source, r.Count)).ToList(),
            BuildTimeseries(timeseriesRows, cutoffUtc, nowUtc));
    }

    private static IReadOnlyList<PlanStatsTimeseriesPointDto> BuildTimeseries(
        IReadOnlyList<TimeseriesRow> rows,
        DateTimeOffset cutoffUtc,
        DateTimeOffset nowUtc)
    {
        // Dense fill: точка на каждый день периода, даже если выдач не было — иначе график рвётся.
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
        public long Active_Paying { get; init; }
        public long Canceled { get; init; }
        public long Total { get; init; }
        public long Last_7 { get; init; }
        public long Last_30 { get; init; }
        public long Last_90 { get; init; }
        public long Mrr_Cents { get; init; }
    }

    private sealed record SourceCountRow(string Source, long Count);

    private sealed record TimeseriesRow(DateOnly Day, string Source, long Count);
}
