using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace ProgressService.Core.Features.Admin;

public sealed record GetProgressStatsQuery(DateOnly From, DateOnly To) : IQuery;

public sealed record TopCourseRow(Guid CourseId, long EnrollmentCount);

public sealed record ProgressStatsResponse(
    long TotalEnrollments,
    long ActiveEnrollments,
    long NewEnrollmentsThisWeek,
    long ActiveUsersThisWeek,
    long TotalSubmissions,
    long SubmissionsThisWeek,
    IReadOnlyList<TopCourseRow> TopCourses,
    DateOnly RangeFrom,
    DateOnly RangeTo,
    long NewEnrollmentsInRange,
    long ActiveUsersInRange,
    long SubmissionsInRange);

public sealed class GetProgressStatsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/admin/stats",
                async Task<EndpointResult<ProgressStatsResponse>> (
                    [FromServices] GetProgressStatsHandler handler,
                    [FromQuery] DateOnly? from,
                    [FromQuery] DateOnly? to,
                    CancellationToken ct) =>
                await handler.Handle(AdminStatsRange.Resolve(from, to), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

// Mirror of the same helper in AuthService.Core.Features.Admin.Queries and
// AccessService.Core.Features.Admin — keep `DefaultDays` / `MaxDays` /
// clamping rules in sync across all three. Internal-per-service so it can't
// live in Shared/ without dragging Wolverine/PlatformDatabase coupling.
internal static class AdminStatsRange
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    public static GetProgressStatsQuery Resolve(DateOnly? from, DateOnly? to)
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

        return new GetProgressStatsQuery(resolvedFrom, resolvedTo);
    }
}

public sealed class GetProgressStatsHandler
    : IQueryHandlerWithResult<ProgressStatsResponse, GetProgressStatsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetProgressStatsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ProgressStatsResponse, Error>> Handle(
        GetProgressStatsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string countsSql = """
            SELECT
                (SELECT COUNT(*) FROM course_enrollments) AS total_enrollments,
                -- access-derive-model (#367): archive concept gone. Every enrollment row is now a
                -- progress anchor (= an engaged user); "active" == total.
                (SELECT COUNT(*) FROM course_enrollments) AS active_enrollments,
                (SELECT COUNT(*) FROM course_enrollments WHERE enrolled_at >= NOW() - INTERVAL '7 days') AS new_enrollments_week,
                (SELECT COUNT(DISTINCT user_id) FROM material_views WHERE viewed_at >= NOW() - INTERVAL '7 days') AS active_users_week,
                (SELECT COUNT(*) FROM issue_submissions) AS total_submissions,
                (SELECT COUNT(*) FROM issue_submissions WHERE submitted_at >= NOW() - INTERVAL '7 days') AS submissions_week,
                (SELECT COUNT(*) FROM course_enrollments
                    WHERE enrolled_at::date BETWEEN @From AND @To
                ) AS new_enrollments_in_range,
                (SELECT COUNT(DISTINCT user_id) FROM material_views
                    WHERE viewed_at::date BETWEEN @From AND @To
                ) AS active_users_in_range,
                (SELECT COUNT(*) FROM issue_submissions
                    WHERE submitted_at::date BETWEEN @From AND @To
                ) AS submissions_in_range;
            """;

        const string topCoursesSql = """
            SELECT course_id, COUNT(*) AS enrollment_count
            FROM course_enrollments
            GROUP BY course_id
            ORDER BY enrollment_count DESC
            LIMIT 5;
            """;

        // Dapper 2.1.66 cannot bind DateOnly as a parameter — convert to DateTime
        // for the wire. PG cast against `created_at::date` handles the rest.
        var rangeParams = new
        {
            From = query.From.ToDateTime(TimeOnly.MinValue),
            To = query.To.ToDateTime(TimeOnly.MinValue),
        };

        CommandDefinition countsCommand = new(
            countsSql,
            rangeParams,
            cancellationToken: cancellationToken);
        CommandDefinition topCoursesCommand = new(
            topCoursesSql,
            cancellationToken: cancellationToken);

        CountsRow counts = await connection.QuerySingleAsync<CountsRow>(countsCommand);
        IEnumerable<TopCourseRow> top = await connection.QueryAsync<TopCourseRow>(topCoursesCommand);

        return new ProgressStatsResponse(
            counts.TotalEnrollments,
            counts.ActiveEnrollments,
            counts.NewEnrollmentsWeek,
            counts.ActiveUsersWeek,
            counts.TotalSubmissions,
            counts.SubmissionsWeek,
            top.ToList(),
            query.From,
            query.To,
            counts.NewEnrollmentsInRange,
            counts.ActiveUsersInRange,
            counts.SubmissionsInRange);
    }

    private sealed record CountsRow
    {
        public long TotalEnrollments { get; init; }
        public long ActiveEnrollments { get; init; }
        public long NewEnrollmentsWeek { get; init; }
        public long ActiveUsersWeek { get; init; }
        public long TotalSubmissions { get; init; }
        public long SubmissionsWeek { get; init; }
        public long NewEnrollmentsInRange { get; init; }
        public long ActiveUsersInRange { get; init; }
        public long SubmissionsInRange { get; init; }
    }
}
