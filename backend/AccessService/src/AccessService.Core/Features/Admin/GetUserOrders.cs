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

public sealed record GetUserOrdersForAdminQuery(Guid UserId) : IQuery;

public sealed record AdminUserOrderRow(
    Guid Id,
    Guid PlanId,
    string? PlanDisplayName,
    long AmountCents,
    string Currency,
    string Status,
    string Provider,
    string? ExternalProviderRef,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string? FailureReason);

public sealed record AdminUserOrdersResponse(IReadOnlyList<AdminUserOrderRow> Items);

public sealed class GetUserOrdersForAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/users/{userId:guid}/orders",
                async Task<EndpointResult<AdminUserOrdersResponse>> (
                    Guid userId,
                    [FromServices] GetUserOrdersForAdminHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserOrdersForAdminQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed class GetUserOrdersForAdminHandler
    : IQueryHandlerWithResult<AdminUserOrdersResponse, GetUserOrdersForAdminQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserOrdersForAdminHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserOrdersResponse, Error>> Handle(
        GetUserOrdersForAdminQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                o.id,
                o.plan_id,
                p.display_name AS plan_display_name,
                o.amount_cents,
                o.currency,
                o.status,
                o.provider,
                o.external_provider_ref,
                o.created_at,
                o.paid_at,
                o.failure_reason
            FROM orders o
            LEFT JOIN plans p ON p.id = o.plan_id
            WHERE o.user_id = @UserId
            ORDER BY o.created_at DESC
            LIMIT 200;
            """;

        IEnumerable<AdminUserOrderRow> rows =
            await connection.QueryAsync<AdminUserOrderRow>(sql, new { UserId = query.UserId });

        return new AdminUserOrdersResponse(rows.ToList());
    }
}
