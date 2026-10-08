using System.Data.Common;
using AuthService.Contracts.Admin;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

public sealed record GetAdminStatsQuery(DateOnly From, DateOnly To) : IQuery;

public sealed class GetAdminStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/admin/stats", async Task<EndpointResult<AdminStatsResponse>> (
                    [FromServices] GetAdminStatsHandler handler,
                    [FromQuery] DateOnly? from,
                    [FromQuery] DateOnly? to,
                    CancellationToken ct) =>
                await handler.Handle(AdminStatsRange.Resolve(from, to), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

// Mirror of the same helper in AccessService.Core.Features.Admin and
// ProgressService.Core.Features.Admin — keep `DefaultDays` / `MaxDays` /
// clamping rules in sync across all three. Internal-per-service so it can't
// live in Shared/ without dragging Wolverine/PlatformDatabase coupling.
internal static class AdminStatsRange
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    public static GetAdminStatsQuery Resolve(DateOnly? from, DateOnly? to)
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

        return new GetAdminStatsQuery(resolvedFrom, resolvedTo);
    }
}

public sealed class GetAdminStatsHandler : IQueryHandlerWithResult<AdminStatsResponse, GetAdminStatsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAdminStatsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminStatsResponse, Error>> Handle(
        GetAdminStatsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string countsSql = """
            SELECT
                COUNT(*) AS total,
                COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '1 day') AS new_today,
                COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '7 days') AS new_week,
                COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '30 days') AS new_month,
                COUNT(*) FILTER (WHERE last_login_at >= NOW() - INTERVAL '7 days') AS active_week,
                COUNT(*) FILTER (WHERE email_confirmed) AS confirmed,
                COUNT(*) FILTER (WHERE lockout_end IS NOT NULL AND lockout_end > NOW()) AS locked,
                COUNT(*) FILTER (WHERE created_at::date BETWEEN @From AND @To) AS new_in_range
            FROM users;
            """;

        const string rolesSql = """
            SELECT r.name AS role_name, COUNT(*) AS cnt
            FROM user_roles ur
            JOIN roles r ON r.id = ur.role_id
            GROUP BY r.name;
            """;

        // generate_series guarantees one row per day in the range — empty days
        // render as count=0 so the chart line stays continuous.
        const string dailySql = """
            WITH days AS (
                SELECT generate_series(@From::date, @To::date, '1 day')::date AS day
            )
            SELECT d.day AS day, COUNT(u.id) AS count
            FROM days d
            LEFT JOIN users u ON u.created_at::date = d.day
            GROUP BY d.day
            ORDER BY d.day;
            """;

        // Dapper 2.1.66 cannot bind DateOnly as a parameter — convert to DateTime
        // for the wire. PG cast (`::date` / `BETWEEN @From AND @To` against
        // `created_at::date`) handles the rest.
        var rangeParams = new
        {
            From = query.From.ToDateTime(TimeOnly.MinValue),
            To = query.To.ToDateTime(TimeOnly.MinValue),
        };

        CountsRow counts = await connection.QuerySingleAsync<CountsRow>(countsSql, rangeParams);
        IEnumerable<RoleCountRow> roleRows = await connection.QueryAsync<RoleCountRow>(rolesSql);
        IEnumerable<DailyRow> dailyRows = await connection.QueryAsync<DailyRow>(dailySql, rangeParams);

        Dictionary<string, long> byRole = roleRows.ToDictionary(r => r.RoleName, r => r.Cnt);
        List<DailyRegistrationDto> daily = dailyRows
            .Select(d => new DailyRegistrationDto(
                d.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                d.Count))
            .ToList();

        return new AdminStatsResponse(
            counts.Total,
            counts.NewToday,
            counts.NewWeek,
            counts.NewMonth,
            counts.ActiveWeek,
            counts.Confirmed,
            counts.Locked,
            byRole,
            daily,
            query.From,
            query.To,
            counts.NewInRange);
    }

    private sealed record CountsRow
    {
        public long Total { get; init; }
        public long NewToday { get; init; }
        public long NewWeek { get; init; }
        public long NewMonth { get; init; }
        public long ActiveWeek { get; init; }
        public long Confirmed { get; init; }
        public long Locked { get; init; }
        public long NewInRange { get; init; }
    }

    private sealed record RoleCountRow
    {
        public string RoleName { get; init; } = string.Empty;
        public long Cnt { get; init; }
    }

    private sealed record DailyRow
    {
        public DateOnly Day { get; init; }
        public long Count { get; init; }
    }
}
