using System.Security.Claims;
using AuthService.Core.Options;
using Framework.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Auth.GitHub;

public sealed class GitHubSyncCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/github/sync-courses", async (
                HttpContext httpContext,
                IOptions<AuthServiceOptions> authOptions) =>
            {
                // Browser navigation — try cookie first, then JWT.
                AuthenticateResult cookieAuth = await httpContext.AuthenticateAsync(
                    IdentityConstants.ApplicationScheme);
                AuthenticateResult jwtAuth = await httpContext.AuthenticateAsync(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

                var principal = cookieAuth.Succeeded ? cookieAuth.Principal
                    : jwtAuth.Succeeded ? jwtAuth.Principal
                    : null;

                string rawUserId = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? principal?.FindFirstValue(GitHubClaims.SUB)
                                   ?? "";

                Guid userId = Guid.TryParse(rawUserId, out Guid parsed) ? parsed : Guid.Empty;

                // Mobile browsers (iOS Safari ITP, Set-Cookie on 302 unreliability)
                // sometimes drop the Identity cookie even though the NextAuth session stays
                // alive. GitHub LOGIN flow is disabled (#696), so there is no challenge to
                // fall back to — send the user to the login page to re-authenticate via
                // email; callbackUrl brings them back to the integrations page to retry.
                if (userId == Guid.Empty)
                    return Results.Redirect(
                        $"{authOptions.Value.FrontendBaseUrl}/login?callbackUrl=%2Fsettings%2Fintegrations");

                var properties = new AuthenticationProperties
                {
                    RedirectUri = GitHubRoutes.OIDC_CALLBACK,
                    Items =
                    {
                        [GitHubRoutes.FLOW_KEY] = GitHubRoutes.FLOW_SYNC,
                        [GitHubRoutes.USER_ID_KEY] = userId.ToString(),
                    },
                };

                return Results.Challenge(properties, [GitHubRoutes.PROVIDER_NAME]);
            })
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("github");
    }
}
