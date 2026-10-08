using AuthService.Core.Options;
using AuthService.Core.Services;
using AuthService.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Client.AspNetCore;
using Results = Microsoft.AspNetCore.Http.Results;

namespace AuthService.Core.Features.Auth.GitHub;

public sealed class GitHubSyncHandler(
    UserManager<Account> userManager,
    IOptions<AuthServiceOptions> authOptions,
    ILogger<AuthAudit> audit,
    GitHubOrgSyncService orgSyncService)
{
    private readonly string _frontendBaseUrl = authOptions.Value.FrontendBaseUrl;

    public async Task<IResult> HandleAsync(
        AuthenticateResult authResult,
        string providerKey,
        CancellationToken ct)
    {
        string? accessToken = authResult.Properties?.GetTokenValue(
            OpenIddictClientAspNetCoreConstants.Tokens.BackchannelAccessToken);

        string? userId = null;
        authResult.Properties?.Items.TryGetValue(GitHubRoutes.USER_ID_KEY, out userId);
        if (string.IsNullOrEmpty(userId))
            return Results.Redirect($"{_frontendBaseUrl}/settings?sync=error");

        Account? user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.Redirect($"{_frontendBaseUrl}/settings?sync=error");

        // Verify the GitHub login belongs to this user
        Account? loginOwner = await userManager.FindByLoginAsync(
            GitHubRoutes.PROVIDER_NAME, providerKey);
        if (loginOwner is null || loginOwner.Id != user.Id)
            return Results.Redirect($"{_frontendBaseUrl}/settings?sync=error");

        int enrolledCount = await orgSyncService.TrySyncAsync(
            user.Id, user.UserName, accessToken, ct);

        audit.LogGitHubSyncCompleted(user.Id, enrolledCount);

        string result = enrolledCount > 0 ? "success" : "no_orgs";
        return Results.Redirect($"{_frontendBaseUrl}/settings?sync={result}");
    }
}
