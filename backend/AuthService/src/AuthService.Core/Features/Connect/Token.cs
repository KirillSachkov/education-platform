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
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Connect;

public sealed class TokenEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("connect/token", HandleAsync)
            .AllowAnonymous()
            .RequireRateLimiting("token");
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<Account> userManager,
        IOptions<OpenIddictOptions> openIddictOptions,
        IOpenIddictScopeManager scopeManager)
    {
        // boundary: OpenIddict middleware populates the request; null means the endpoint was hit outside the OIDC pipeline.
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        if (request.IsClientCredentialsGrantType())
            return await HandleClientCredentialsAsync(request, openIddictOptions.Value, scopeManager);

        if (request.IsAuthorizationCodeGrantType() || request.IsRefreshTokenGrantType())
            return await HandleCodeOrRefreshAsync(httpContext, userManager, openIddictOptions.Value, scopeManager);

        return Results.BadRequest(new { error = "unsupported_grant_type" });
    }

    private static async Task<IResult> HandleClientCredentialsAsync(
        OpenIddictRequest request,
        OpenIddictOptions openIddictOptions,
        IOpenIddictScopeManager scopeManager)
    {
        ClaimsIdentity identity = new(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            OpenIddictConstants.Claims.Name,
            OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, request.ClientId);

        // Admin-tool client (MCP) gets platform-admin role; everything else gets
        // platform-service. Distinct roles keep admin actions identifiable in audit logs.
        //
        // Security invariant (load-bearing): we trust request.ClientId here because
        // OpenIddict's client-authentication middleware has already validated the
        // client_id + client_secret against openiddict_applications before this handler
        // runs. An unauthenticated request with a forged ClientId never reaches this
        // point — it's rejected upstream with invalid_client.
        bool isAdminClient = !string.IsNullOrEmpty(openIddictOptions.AdminApi.ClientId)
            && string.Equals(
                request.ClientId, openIddictOptions.AdminApi.ClientId, StringComparison.Ordinal);
        identity.AddClaim("roles", isAdminClient ? PlatformRoles.ADMIN : PlatformRoles.SERVICE);

        identity.SetScopes(request.GetScopes());

        List<string> resources = await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync();
        identity.SetResources(resources);
        identity.SetAudiences(resources);

        identity.SetDestinations(static _ => [OpenIddictConstants.Destinations.AccessToken]);

        return Results.SignIn(
            new ClaimsPrincipal(identity),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> HandleCodeOrRefreshAsync(
        HttpContext httpContext,
        UserManager<Account> userManager,
        OpenIddictOptions openIddictOptions,
        IOpenIddictScopeManager scopeManager)
    {
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()!;

        ClaimsPrincipal principal = (await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)).Principal!;

        string? userId = principal.GetClaim(OpenIddictConstants.Claims.Subject);
        if (userId is null)
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        Account? user = await userManager.FindByIdAsync(userId);
        if (user is null)
            return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        // Validate security stamp on refresh token grants
        if (request.IsRefreshTokenGrantType())
        {
            string? stampFromToken = principal.FindFirst(PlatformClaims.SECURITY_STAMP)?.Value;
            if (stampFromToken is not null && !string.Equals(stampFromToken, user.SecurityStamp, StringComparison.Ordinal))
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

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
        // so display_name="" survives a refresh_token grant and keeps the onboarding signal alive.
        identity.AddClaim(new Claim(PlatformClaims.DISPLAY_NAME, user.DisplayName ?? string.Empty));

        foreach (string role in roles)
            identity.AddClaim("roles", role);

        // Private claim — stored in refresh token only, no destination
        identity.SetClaim(PlatformClaims.SECURITY_STAMP, user.SecurityStamp);

        identity.SetScopes(principal.GetScopes());

        List<string> resources = await scopeManager.ListResourcesAsync(identity.GetScopes()).ToListAsync();
        identity.SetResources(resources);
        identity.SetAudiences(resources);

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

        return Results.SignIn(
            new ClaimsPrincipal(identity),
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
