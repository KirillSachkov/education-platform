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

public sealed class GitHubLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/github/link", async (
                HttpContext httpContext,
                IOptions<AuthServiceOptions> authOptions) =>
            {
                string frontendUrl = authOptions.Value.FrontendBaseUrl;

                // Browser navigation — try cookie first, then JWT.
                // If neither works, redirect instead of returning 401 (which mobile browsers download).
                AuthenticateResult cookieAuth = await httpContext.AuthenticateAsync(
                    IdentityConstants.ApplicationScheme);
                AuthenticateResult jwtAuth = await httpContext.AuthenticateAsync(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);

                var principal = cookieAuth.Succeeded ? cookieAuth.Principal
                    : jwtAuth.Succeeded ? jwtAuth.Principal
                    : null;

                if (principal is null)
                    return Results.Redirect($"{frontendUrl}/profile?account=github-link-failed");

                string rawUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                                   ?? principal.FindFirstValue(GitHubClaims.SUB)
                                   ?? "";

                if (!Guid.TryParse(rawUserId, out Guid userId) || userId == Guid.Empty)
                    return Results.Redirect($"{frontendUrl}/profile?account=github-link-failed");

                var properties = new AuthenticationProperties
                {
                    RedirectUri = GitHubRoutes.OIDC_CALLBACK,
                    Items =
                    {
                        [GitHubRoutes.FLOW_KEY] = GitHubRoutes.FLOW_LINK,
                        [GitHubRoutes.USER_ID_KEY] = userId.ToString(),
                    },
                };

                return Results.Challenge(properties, [GitHubRoutes.PROVIDER_NAME]);
            })
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("github");
    }
}
