using System.Data.Common;
using AuthService.Contracts;
using AuthService.Core.Features.Auth.GitHub;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.InternalUsers.Queries;

public sealed record GetUserGithubLoginQuery(Guid UserId) : IQuery;

public sealed class GetUserGithubLoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/internal/users/{userId:guid}/github-login/",
                async Task<EndpointResult<UserGithubLoginResponse>> (
                    [FromRoute] Guid userId,
                    [FromServices] GetUserGithubLoginHandler handler,
                    CancellationToken ct) =>
                    await handler.Handle(new GetUserGithubLoginQuery(userId), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class GetUserGithubLoginHandler
    : IQueryHandlerWithResult<UserGithubLoginResponse, GetUserGithubLoginQuery>
{
    private readonly ITransactionManager _transactions;

    public GetUserGithubLoginHandler(ITransactionManager transactions) =>
        _transactions = transactions;

    public async Task<Result<UserGithubLoginResponse, Error>> Handle(
        GetUserGithubLoginQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactions.GetDbConnection();
        const string sql = """
            SELECT up.role_profiles AS Profiles
            FROM user_profiles up
            WHERE up.id = @UserId
              AND EXISTS (
                  SELECT 1
                  FROM user_logins ul
                  WHERE ul.user_id = up.id
                    AND ul.login_provider = @Provider)
            LIMIT 1
            """;
        GithubProfileRow? row = await connection.QueryFirstOrDefaultAsync<GithubProfileRow>(
            sql,
            new { query.UserId, Provider = GitHubRoutes.PROVIDER_NAME });
        string? login = ExtractLogin(row?.Profiles?.Student?.GitHubUrl);
        return new UserGithubLoginResponse(query.UserId, login);
    }

    private static string? ExtractLogin(string? githubUrl)
    {
        if (!Uri.TryCreate(githubUrl, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 1 ? segments[0] : null;
    }

    private sealed class GithubProfileRow
    {
        public ProfilesDto? Profiles { get; init; }
    }
}
