using System.Data.Common;
using AuthService.Core.Features.Auth.GitHub;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Admin.Queries;

/// <summary>
/// <c>GET /users/{userId}/github-status</c> (#444) — admin/support snapshot of a user's GitHub
/// identity for the «Доступы и сообщества» post-purchase panel: linked-or-not, GitHub login id +
/// username (Identity external login, provider "GitHub"), and cached org-slugs
/// (<c>user_github_orgs</c>, the same ones driving plan auto-enrollment). Read-only; mirrors the
/// <see cref="GetUserDetailHandler"/> Dapper pattern. Complements the ARS installations read
/// (GitHub App / AI-review status) and the per-grant <c>githubStepPending</c> onboarding chip.
/// </summary>
public sealed record GetUserGithubStatusQuery(Guid UserId) : IQuery;

/// <summary>Одна GitHub-организация пользователя из кэша <c>user_github_orgs</c>.</summary>
public sealed record AdminUserGithubOrgDto(string Slug, DateTime SyncedAt);

public sealed record AdminUserGithubStatusDto(
    bool Linked,
    string? GithubUserId,
    string? GithubUsername,
    IReadOnlyList<AdminUserGithubOrgDto> Orgs);

public sealed class GetUserGithubStatusEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/{userId:guid}/github-status",
                async Task<EndpointResult<AdminUserGithubStatusDto>> (
                    Guid userId,
                    [FromServices] GetUserGithubStatusHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetUserGithubStatusQuery(userId), ct))
            .RequirePermissions(PlatformPermissions.Users.VIEW);
}

public sealed class GetUserGithubStatusHandler
    : IQueryHandlerWithResult<AdminUserGithubStatusDto, GetUserGithubStatusQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetUserGithubStatusHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<AdminUserGithubStatusDto, Error>> Handle(
        GetUserGithubStatusQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT provider_key, provider_display_name
                           FROM user_logins
                           WHERE user_id = @UserId AND login_provider = @Provider
                           LIMIT 1;

                           SELECT org_slug AS Slug, synced_at AS SyncedAt
                           FROM user_github_orgs
                           WHERE user_id = @UserId
                           ORDER BY org_slug;
                           """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            sql, new { UserId = query.UserId, Provider = GitHubRoutes.PROVIDER_NAME });

        GithubLoginRow? login = await multi.ReadFirstOrDefaultAsync<GithubLoginRow>();
        List<AdminUserGithubOrgDto> orgs = (await multi.ReadAsync<AdminUserGithubOrgDto>()).ToList();

        return new AdminUserGithubStatusDto(
            Linked: login is not null,
            GithubUserId: login?.ProviderKey,
            GithubUsername: login?.ProviderDisplayName,
            Orgs: orgs);
    }

    private sealed record GithubLoginRow
    {
        public string? ProviderKey { get; init; }
        public string? ProviderDisplayName { get; init; }
    }
}
