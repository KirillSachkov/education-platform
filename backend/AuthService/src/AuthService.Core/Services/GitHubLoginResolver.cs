using System.Security.Claims;
using AuthService.Core.Features.Auth.GitHub;

namespace AuthService.Core.Services;

public sealed class GitHubLoginResolver(IGitHubUserService gitHubUserService)
{
    public async Task<string?> ResolveAsync(
        ClaimsPrincipal? principal,
        string? accessToken,
        CancellationToken ct)
    {
        if (principal is not null)
        {
            string? login = principal.FindFirstValue(GitHubClaims.PREFERRED_USERNAME)
                            ?? principal.FindFirstValue(GitHubClaims.URN_LOGIN)
                            ?? principal.FindFirstValue(GitHubClaims.LOGIN);

            if (!string.IsNullOrEmpty(login))
                return login;
        }

        if (string.IsNullOrEmpty(accessToken))
            return null;

        var result = await gitHubUserService.GetLoginAsync(accessToken, ct);
        return result.IsSuccess ? result.Value : null;
    }
}
