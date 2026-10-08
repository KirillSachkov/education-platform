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

public sealed record GetUserEnrollmentsForAdminQuery(Guid UserId) : IQuery;

public sealed record AdminUserEnrollmentRow(
    Guid CourseId,
    Guid? AuthorId,
    DateTime EnrolledAt,
    string Source);

public sealed record AdminUserEnrollmentsResponse(IReadOnlyList<AdminUserEnrollmentRow> Items);

public sealed class GetUserEnrollmentsForAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/admin/users/{userId:guid}/enrollments",
                async Task<EndpointResult<AdminUserEnrollmentsResponse>> (
                    Guid userId,
                    [FromServices] GetUserEnrollmentsForAdminHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserEnrollmentsForAdminQuery(userId), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUserEnrollmentsForAdminHandler
    : IQueryHandlerWithResult<AdminUserEnrollmentsResponse, GetUserEnrollmentsForAdminQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserEnrollmentsForAdminHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserEnrollmentsResponse, Error>> Handle(
        GetUserEnrollmentsForAdminQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                course_id,
                author_id,
                enrolled_at,
                source
            FROM course_enrollments
            WHERE user_id = @UserId
            ORDER BY enrolled_at DESC
            LIMIT 200;
            """;

        CommandDefinition command = new(
            sql,
            new { UserId = query.UserId },
            cancellationToken: cancellationToken);

        IEnumerable<AdminUserEnrollmentRow> rows =
            await connection.QueryAsync<AdminUserEnrollmentRow>(command);

        return new AdminUserEnrollmentsResponse(rows.ToList());
    }
}
