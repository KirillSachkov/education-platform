using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.Admin;

public sealed record GetAccessStatsQuery(DateOnly From, DateOnly To) : IQuery;

public sealed record AdminAccessByPlanRow
{
    public Guid PlanId { get; init; }
    public string? DisplayName { get; init; }
    public long ActiveGrantsCount { get; init; }
}

public sealed record AdminAccessStatsResponse(
    long TotalRevenueCents,
    long PaidOrdersCount,
    long FailedOrdersCount,
    long ActiveGrantsCount,
    long ExpiredGrantsCount,
    long RevokedGrantsCount,
    IReadOnlyList<AdminAccessByPlanRow> TopPlans,
    DateOnly RangeFrom,
    DateOnly RangeTo,
    long RevenueInRangeCents,
    long PaidOrdersInRangeCount,
    long FailedOrdersInRangeCount);

public sealed class GetAccessStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/stats",
                async Task<EndpointResult<AdminAccessStatsResponse>> (
                    [FromServices] GetAccessStatsHandler handler,
                    [FromQuery] DateOnly? from,
                    [FromQuery] DateOnly? to,
                    CancellationToken ct) =>
                await handler.Handle(AdminStatsRange.Resolve(from, to), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

// Mirror of the same helper in AuthService.Core.Features.Admin.Queries and
// ProgressService.Core.Features.Admin — keep `DefaultDays` / `MaxDays` /
// clamping rules in sync across all three. Internal-per-service so it can't
// live in Shared/ without dragging Wolverine/PlatformDatabase coupling.
internal static class AdminStatsRange
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    public static GetAccessStatsQuery Resolve(DateOnly? from, DateOnly? to)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        DateOnly resolvedTo = to ?? today;
        DateOnly resolvedFrom = from ?? resolvedTo.AddDays(-(DefaultDays - 1));

        if (resolvedFrom > resolvedTo)
        {
            (resolvedFrom, resolvedTo) = (resolvedTo, resolvedFrom);
        }

        int span = resolvedTo.DayNumber - resolvedFrom.DayNumber + 1;
        if (span > MaxDays)
        {
            resolvedFrom = resolvedTo.AddDays(-(MaxDays - 1));
        }

        return new GetAccessStatsQuery(resolvedFrom, resolvedTo);
    }
}

public sealed class GetAccessStatsHandler
    : IQueryHandlerWithResult<AdminAccessStatsResponse, GetAccessStatsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAccessStatsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminAccessStatsResponse, Error>> Handle(
        GetAccessStatsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string countsSql = """
            SELECT
                COALESCE((SELECT SUM(amount_cents) FROM orders WHERE status = 'PAID'), 0) AS total_revenue_cents,
                (SELECT COUNT(*) FROM orders WHERE status = 'PAID') AS paid_orders_count,
                (SELECT COUNT(*) FROM orders WHERE status = 'FAILED') AS failed_orders_count,
                (SELECT COUNT(*) FROM plan_grants WHERE status = 'ACTIVE') AS active_grants_count,
                (SELECT COUNT(*) FROM plan_grants WHERE status = 'EXPIRED') AS expired_grants_count,
                (SELECT COUNT(*) FROM plan_grants WHERE status = 'REVOKED') AS revoked_grants_count,
                COALESCE((
                    SELECT SUM(amount_cents) FROM orders
                    WHERE status = 'PAID' AND created_at::date BETWEEN @From AND @To
                ), 0) AS revenue_in_range_cents,
                (SELECT COUNT(*) FROM orders
                    WHERE status = 'PAID' AND created_at::date BETWEEN @From AND @To
                ) AS paid_orders_in_range_count,
                (SELECT COUNT(*) FROM orders
                    WHERE status = 'FAILED' AND created_at::date BETWEEN @From AND @To
                ) AS failed_orders_in_range_count;
            """;

        const string topPlansSql = """
            SELECT g.plan_id, p.display_name, COUNT(*) AS active_grants_count
            FROM plan_grants g
            LEFT JOIN plans p ON p.id = g.plan_id
            WHERE g.status = 'ACTIVE'
            GROUP BY g.plan_id, p.display_name
            ORDER BY active_grants_count DESC
            LIMIT 5;
            """;

        // Dapper 2.1.66 cannot bind DateOnly as a parameter — convert to DateTime
        // for the wire. PG cast against `created_at::date` handles the rest.
        var rangeParams = new
        {
            From = query.From.ToDateTime(TimeOnly.MinValue),
            To = query.To.ToDateTime(TimeOnly.MinValue),
        };

        CountsRow counts = await connection.QuerySingleAsync<CountsRow>(countsSql, rangeParams);
        IEnumerable<AdminAccessByPlanRow> top =
            await connection.QueryAsync<AdminAccessByPlanRow>(topPlansSql);

        return new AdminAccessStatsResponse(
            counts.TotalRevenueCents,
            counts.PaidOrdersCount,
            counts.FailedOrdersCount,
            counts.ActiveGrantsCount,
            counts.ExpiredGrantsCount,
            counts.RevokedGrantsCount,
            top.ToList(),
            query.From,
            query.To,
            counts.RevenueInRangeCents,
            counts.PaidOrdersInRangeCount,
            counts.FailedOrdersInRangeCount);
    }

    private sealed record CountsRow
    {
        public long TotalRevenueCents { get; init; }
        public long PaidOrdersCount { get; init; }
        public long FailedOrdersCount { get; init; }
        public long ActiveGrantsCount { get; init; }
        public long ExpiredGrantsCount { get; init; }
        public long RevokedGrantsCount { get; init; }
        public long RevenueInRangeCents { get; init; }
        public long PaidOrdersInRangeCount { get; init; }
        public long FailedOrdersInRangeCount { get; init; }
    }
}
