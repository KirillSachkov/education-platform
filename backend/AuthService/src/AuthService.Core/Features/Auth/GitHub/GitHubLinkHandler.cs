using System.Security.Claims;
using AuthService.Core.Options;
using AuthService.Core.Services;
using AuthService.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Client.AspNetCore;
using Results = Microsoft.AspNetCore.Http.Results;

namespace AuthService.Core.Features.Auth.GitHub;

public sealed class GitHubLinkHandler(
    UserManager<Account> userManager,
    SignInManager<Account> signInManager,
    IOptions<AuthServiceOptions> authOptions,
    ILogger<AuthAudit> audit,
    GitHubLoginResolver loginResolver,
    GitHubProfileService profileService,
    GitHubOrgSyncService orgSyncService)
{
    private readonly string _frontendBaseUrl = authOptions.Value.FrontendBaseUrl;

    public async Task<IResult> HandleAsync(
        AuthenticateResult authResult,
        string providerKey,
        CancellationToken ct = default)
    {
        // Все redirect'ы из GitHub-link flow ведут на страницу интеграций — там
        // отрисовывается AccountStatusBanner с человеческим описанием результата.
        string failRedirect = $"{_frontendBaseUrl}/settings/integrations?account=github-link-failed";
        string? accessToken = authResult.Properties?.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken);

        string? userId = null;
        authResult.Properties?.Items.TryGetValue(GitHubRoutes.USER_ID_KEY, out userId);
        if (string.IsNullOrEmpty(userId))
            return Results.Redirect(failRedirect);

        Account? currentUser = await userManager.FindByIdAsync(userId);
        if (currentUser is null)
            return Results.Redirect(failRedirect);

        IList<UserLoginInfo> logins = await userManager.GetLoginsAsync(currentUser);
        UserLoginInfo? currentGitHubLogin = logins.FirstOrDefault(l =>
            string.Equals(l.LoginProvider, GitHubRoutes.PROVIDER_NAME, StringComparison.Ordinal));
        if (currentGitHubLogin is not null)
        {
            // Повторный link тем же GitHub-аккаунтом чинит проекцию (login в профиле и orgs),
            // если она не сохранилась при первой привязке (#1148). Другой аккаунт не трогаем.
            if (string.Equals(currentGitHubLogin.ProviderKey, providerKey, StringComparison.Ordinal))
                await SyncGitHubProfileAsync(currentUser, authResult, accessToken, ct);

            return Results.Redirect($"{_frontendBaseUrl}/settings/integrations?account=github-already-linked");
        }

        Account? existingOwner = await userManager.FindByLoginAsync(
            GitHubRoutes.PROVIDER_NAME, providerKey);
        if (existingOwner is not null && existingOwner.Id != currentUser.Id)
        {
            // Этот GitHub-аккаунт уже привязан к другому пользователю платформы.
            // Отдельный код, чтобы фронт показал точный текст вместо общего «failed».
            audit.LogWarning(
                "GitHub link rejected: provider account {ProviderKey} already belongs to {OwnerId}, " +
                "current user {CurrentUserId}",
                providerKey, existingOwner.Id, currentUser.Id);
            return Results.Redirect(
                $"{_frontendBaseUrl}/settings/integrations?account=github-link-conflict");
        }

        string? name = authResult.Principal?.FindFirstValue(ClaimTypes.Name)
                       ?? authResult.Principal?.FindFirstValue(GitHubClaims.NAME);

        IdentityResult addResult = await userManager.AddLoginAsync(
            currentUser,
            new UserLoginInfo(GitHubRoutes.PROVIDER_NAME, providerKey, name));

        if (!addResult.Succeeded)
            return Results.Redirect(failRedirect);

        // AddLoginAsync updates the security stamp, invalidating the Identity cookie.
        await signInManager.RefreshSignInAsync(currentUser);

        audit.LogGitHubLinked(currentUser.Id);

        await SyncGitHubProfileAsync(currentUser, authResult, accessToken, ct);

        return Results.Redirect(
            $"{_frontendBaseUrl}/settings/integrations?account=github-linked");
    }

    private async Task SyncGitHubProfileAsync(
        Account user, AuthenticateResult authResult, string? accessToken, CancellationToken ct)
    {
        string? login = await loginResolver.ResolveAsync(authResult.Principal, accessToken, ct);

        if (!string.IsNullOrEmpty(login))
            await profileService.SetGitHubUrlAsync(user.Id, login, ct);

        await TrySyncCoursesAsync(user.Id, user.UserName, accessToken, ct);
    }

    private async Task TrySyncCoursesAsync(
        Guid userId, string? username, string? accessToken, CancellationToken ct)
    {
        try
        {
            await orgSyncService.TrySyncAsync(userId, username, accessToken, ct);
        }
        catch (Exception ex)
        {
            audit.LogWarning(
                ex,
                "GitHub course auto-sync failed during link for user {UserId}",
                userId);
        }
    }
}
