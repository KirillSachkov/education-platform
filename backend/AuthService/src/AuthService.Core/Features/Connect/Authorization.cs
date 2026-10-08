using System.Security.Claims;
using AuthService.Core.Options;
using AuthService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace AuthService.Core.Features.Connect;

public sealed class AuthorizationEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("connect/authorize", HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous-read");
        app.MapPost("connect/authorize", HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting("anonymous-read");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        SignInManager<Account> signInManager,
        IOptions<OpenIddictOptions> openIddictOptions,
        IOpenIddictScopeManager scopeManager)
    {
        // boundary: OpenIddict middleware populates the request; null means the endpoint was hit outside the OIDC pipeline.
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        AuthenticateResult authResult = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        ClaimsPrincipal? result = authResult.Principal;

        if (result is null || !result.Identity!.IsAuthenticated)
            return Challenge(httpContext);

        Account? user = await signInManager.ValidateSecurityStampAsync(result);
        if (user is null ||
            !await signInManager.CanSignInAsync(user) ||
            await signInManager.UserManager.IsLockedOutAsync(user))
        {
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return Challenge(httpContext);
        }

        UserManager<Account> userManager = signInManager.UserManager;
        IList<string> roles = await userManager.GetRolesAsync(user);

        ClaimsIdentity identity = new(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString())
            .SetClaim(OpenIddictConstants.Claims.Name, user.DisplayName ?? user.UserName)
            .SetClaim(OpenIddictConstants.Claims.Email, user.Email)
            .SetClaim(OpenIddictConstants.Claims.PreferredUsername, user.UserName);

        // OpenIddict's .SetClaim(type, "") drops empty values — emit explicitly via AddClaim,
        // so the onboarding-required signal (display_name === "") survives in id_token+access_token,
        // not just in userinfo (which builds JSON manually). Without this, fresh OTP users get
        // displayName=undefined in the JWT and skip the onboarding redirect entirely.

        identity.AddClaim(new Claim(PlatformClaims.DISPLAY_NAME, user.DisplayName ?? string.Empty));

        foreach (string role in roles)
            identity.AddClaim("roles", role);

        identity.SetScopes(request.GetScopes());
        identity.SetResources(await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync());

        // Private claim — stored in refresh token only, no destination
        identity.SetClaim(PlatformClaims.SECURITY_STAMP, user.SecurityStamp);

        identity.SetDestinations(static claim => claim.Type switch
        {
            OpenIddictConstants.Claims.Subject =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            OpenIddictConstants.Claims.Name =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            OpenIddictConstants.Claims.Email =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            OpenIddictConstants.Claims.PreferredUsername =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            "roles" =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            PlatformClaims.DISPLAY_NAME =>
            [
                OpenIddictConstants.Destinations.AccessToken,
                OpenIddictConstants.Destinations.IdentityToken,
            ],
            PlatformClaims.SECURITY_STAMP => [],
            _ => [OpenIddictConstants.Destinations.AccessToken],
        });

        return Results.SignIn(new ClaimsPrincipal(identity),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IResult Challenge(HttpContext httpContext) =>
        Results.Challenge(
            authenticationSchemes: [IdentityConstants.ApplicationScheme],
            properties: new AuthenticationProperties
            {
                RedirectUri = httpContext.Request.PathBase + httpContext.Request.Path +
                              QueryString.Create(httpContext.Request.Query.ToList()),
            });
}
