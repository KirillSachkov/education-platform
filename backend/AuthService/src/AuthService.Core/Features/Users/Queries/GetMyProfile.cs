using System.Data.Common;
using AuthService.Contracts;
using AuthService.Core.Database;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Dapper;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Users.Queries;

public sealed record GetMyProfileQuery : IQuery;

public sealed class GetMyProfileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/users/me", async Task<EndpointResult<GetMyProfileResponse>> (
                    [Microsoft.AspNetCore.Mvc.FromServices]
                    GetMyProfileHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetMyProfileQuery(), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
}

public sealed class GetMyProfileHandler : IQueryHandlerWithResult<GetMyProfileResponse, GetMyProfileQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly IUserGithubOrgRepository _githubOrgs;

    public GetMyProfileHandler(
        ITransactionManager transactionManager,
        UserScopedData user,
        IUserGithubOrgRepository githubOrgs)
    {
        _transactionManager = transactionManager;
        _user = user;
        _githubOrgs = githubOrgs;
    }

    /// <summary>Возвращает профиль текущего пользователя.</summary>
    public async Task<Result<GetMyProfileResponse, Error>> Handle(
        GetMyProfileQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string combinedSql = """
                                   SELECT
                                       id,
                                       bio,
                                       role_profiles AS profiles,
                                       avatar_id AS AvatarId
                                   FROM user_profiles
                                   WHERE id = @UserId;

                                   SELECT
                                       u.user_name AS UserName,
                                       u.display_name AS DisplayName,
                                       u.password_hash IS NOT NULL AS HasPassword,
                                       EXISTS (
                                           SELECT 1 FROM user_logins ul
                                           WHERE ul.user_id = u.id AND ul.login_provider = @GitHubProvider
                                       ) AS HasGitHubLinked,
                                       EXISTS (
                                           SELECT 1 FROM user_logins ul
                                           WHERE ul.user_id = u.id AND ul.login_provider = @TelegramProvider
                                       ) AS HasTelegramLinked
                                   FROM users u
                                   WHERE u.id = @UserId
                                   """;

        await using var multi = await connection.QueryMultipleAsync(
            combinedSql,
            new
            {
                UserId = _user.UserId,
                GitHubProvider = GitHubRoutes.PROVIDER_NAME,
                TelegramProvider = TelegramProviderConstants.PROVIDER_NAME,
            });

        UserProfileRow? row = await multi.ReadSingleOrDefaultAsync<UserProfileRow>();
        AccountRow? accountRow = await multi.ReadSingleOrDefaultAsync<AccountRow>();

        if (accountRow is null)
            return AuthErrors.UserNotFound();

        IReadOnlyList<string> githubOrgs = accountRow.HasGitHubLinked
            ? await _githubOrgs.GetByUserAsync(_user.UserId, cancellationToken)
            : [];

        return BuildResponse(row, accountRow, githubOrgs);
    }

    private GetMyProfileResponse BuildResponse(
        UserProfileRow? row,
        AccountRow accountRow,
        IReadOnlyList<string> githubOrgs) =>
        new(
            Id: _user.UserId,
            Name: accountRow.DisplayName ?? accountRow.UserName ?? _user.Name,
            DisplayName: accountRow.DisplayName,
            Username: accountRow.UserName ?? "",
            Email: _user.Email,
            Roles: _user.Roles,
            Bio: row?.Bio,
            Profiles: row?.Profiles,
            HasPassword: accountRow.HasPassword,
            HasGitHubLinked: accountRow.HasGitHubLinked,
            HasTelegramLinked: accountRow.HasTelegramLinked,
            AvatarId: row?.AvatarId,
            GithubOrgs: githubOrgs
        );

    private sealed class AccountRow
    {
        public string? UserName { get; init; }
        public string? DisplayName { get; init; }
        public bool HasPassword { get; init; }
        public bool HasGitHubLinked { get; init; }
        public bool HasTelegramLinked { get; init; }
    }
}
