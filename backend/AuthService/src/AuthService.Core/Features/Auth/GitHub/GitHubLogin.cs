using AuthService.Core.Options;
using AuthService.Core.Services;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Auth.GitHub;

/// <summary>
/// GitHub as a LOGIN method is disabled (RF law; issue #696). The endpoint is kept so old
/// bookmarks and cached SPA bundles get a friendly redirect to the login page instead of
/// a 404 or an OAuth challenge. GitHub as a LINKED account stays — see GitHubLink.
/// </summary>
public sealed class GitHubLoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/github/login", (
                IOptions<AuthServiceOptions> authOptions,
                ILogger<AuthAudit> auditLogger) =>
            {
                // Signal for post-rollout observability (spec §5): how many users still
                // try the old GitHub login — feeds the campaign-urgency judgement.
                auditLogger.LogGitHubDisabledLoginAttempt("login-endpoint");
                return Results.Redirect(
                    $"{authOptions.Value.FrontendBaseUrl}{GitHubRoutes.LOGIN_DISABLED_REDIRECT}");
            })
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("github");
    }
}
