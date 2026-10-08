using AuthService.Contracts;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Users.UseCases;

/// <summary>
///     XHR-вариант ручной синхронизации интеграций. Не дёргает GitHub API
///     (для этого есть GET <c>/auth/github/sync-courses</c> с OAuth-редиректом —
///     нужен только если юзер только что вступил в новую org). Здесь мы
///     перепубликуем <c>UserGithubLogin</c> с уже-кэшированными org'ами,
///     чтобы ProgressService подобрал недостающие зачисления, и сообщаем
///     фронту, привязан ли Telegram (тогда фронт сам дёрнет TelegramBotService
///     для resync инвайтов в чаты).
/// </summary>
public sealed record SyncIntegrationsCommand : ICommand;

public sealed class SyncIntegrationsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/users/me/integrations/sync/", async Task<EndpointResult<SyncIntegrationsResponse>> (
                    [Microsoft.AspNetCore.Mvc.FromServices] SyncIntegrationsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new SyncIntegrationsCommand(), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW)
            .RequireRateLimiting("github");
    }
}

public sealed class SyncIntegrationsHandler : ICommandHandler<SyncIntegrationsResponse, SyncIntegrationsCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly GitHubOrgSyncService _orgSyncService;
    private readonly UserScopedData _user;

    public SyncIntegrationsHandler(
        UserManager<Account> userManager,
        GitHubOrgSyncService orgSyncService,
        UserScopedData user)
    {
        _userManager = userManager;
        _orgSyncService = orgSyncService;
        _user = user;
    }

    public async Task<Result<SyncIntegrationsResponse, Error>> Handle(
        SyncIntegrationsCommand command,
        CancellationToken cancellationToken)
    {
        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return GeneralErrors.NotFound(_user.UserId);

        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        bool hasGithub = logins.Any(l => string.Equals(
            l.LoginProvider, GitHubRoutes.PROVIDER_NAME, StringComparison.Ordinal));
        bool hasTelegram = logins.Any(l => string.Equals(
            l.LoginProvider, TelegramProviderConstants.PROVIDER_NAME, StringComparison.Ordinal));

        IReadOnlyList<string> matchedOrgs = [];
        if (hasGithub)
        {
            matchedOrgs = await _orgSyncService.SyncFromCacheAsync(
                user.Id, user.UserName, cancellationToken);
        }

        return new SyncIntegrationsResponse(
            MatchedGithubOrgs: matchedOrgs,
            GithubSyncTriggered: hasGithub,
            TelegramLinked: hasTelegram);
    }
}
