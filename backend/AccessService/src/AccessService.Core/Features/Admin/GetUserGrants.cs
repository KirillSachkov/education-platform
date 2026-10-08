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

public sealed record GetUserGrantsForAdminQuery(Guid UserId) : IQuery;

/// <summary>
/// Row shape для <c>GET /access/admin/users/{userId}/grants</c>. Несёт И legacy-имена
/// (<c>Id</c>/<c>CreatedAt</c> — admin cross-service UI), И pinned-имена из admin
/// plan-revoke контракта (#414: <c>GrantId</c>/<c>GrantedAt</c>). Оба указывают на одни
/// и те же колонки (<c>g.id</c>, <c>g.created_at</c>) — Dapper маппит дубли без доп.
/// запроса. Это позволяет одному эндпоинту обслуживать оба фронт-контракта без route-конфликта.
/// </summary>
public sealed record AdminUserGrantRow(
    Guid Id,
    Guid GrantId,
    Guid PlanId,
    string? PlanDisplayName,
    string? PlanTier,
    Guid PlanAuthorId,
    string Source,
    string Status,
    DateTime CreatedAt,
    DateTime GrantedAt,
    DateTime? ExpiresAt,
    DateTime? RevokedAt,
    string? RevokedReason);

public sealed record AdminUserGrantsResponse(IReadOnlyList<AdminUserGrantRow> Items);

public sealed class GetUserGrantsForAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/access/admin/users/{userId:guid}/grants",
                async Task<EndpointResult<AdminUserGrantsResponse>> (
                    Guid userId,
                    [FromServices] GetUserGrantsForAdminHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserGrantsForAdminQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.MODERATOR);
}

public sealed class GetUserGrantsForAdminHandler
    : IQueryHandlerWithResult<AdminUserGrantsResponse, GetUserGrantsForAdminQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserGrantsForAdminHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserGrantsResponse, Error>> Handle(
        GetUserGrantsForAdminQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // NB: plan_grants has `granted_at` + `revoke_reason` columns (no `created_at` /
        // `revoked_reason`). Both legacy (`created_at`) and pinned (`granted_at`) DTO fields
        // alias the same `granted_at` column; `revoked_reason` DTO field maps from `revoke_reason`.
        const string sql = """
            SELECT
                g.id,
                g.id AS grant_id,
                g.plan_id,
                p.display_name AS plan_display_name,
                p.tier AS plan_tier,
                p.author_id AS plan_author_id,
                g.source,
                g.status,
                g.granted_at AS created_at,
                g.granted_at,
                g.expires_at,
                g.revoked_at,
                g.revoke_reason AS revoked_reason
            FROM plan_grants g
            LEFT JOIN plans p ON p.id = g.plan_id
            WHERE g.user_id = @UserId
            ORDER BY g.granted_at DESC
            LIMIT 200;
            """;

        IEnumerable<AdminUserGrantRow> rows =
            await connection.QueryAsync<AdminUserGrantRow>(sql, new { UserId = query.UserId });

        return new AdminUserGrantsResponse(rows.ToList());
    }
}
