using System.Security.Claims;
using AuthService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace AuthService.Core.Features.Connect;

public sealed class UserinfoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("connect/userinfo", HandleAsync)
            .AllowAnonymous();
        app.MapPost("connect/userinfo", HandleAsync)
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        UserManager<Account> userManager)
    {
        AuthenticateResult authenticateResult = await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        ClaimsPrincipal? principal = authenticateResult.Principal;
        if (!authenticateResult.Succeeded || principal is null)
            return Results.Challenge(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        string? userId = principal.GetClaim(OpenIddictConstants.Claims.Subject);
        Account? user = userId is not null ? await userManager.FindByIdAsync(userId) : null;

        if (user is null)
            return Results.Challenge(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

        IList<string> roles = await userManager.GetRolesAsync(user);

        var claims = new Dictionary<string, object>
        {
            [OpenIddictConstants.Claims.Subject] = user.Id.ToString(),
            [OpenIddictConstants.Claims.Name] = user.DisplayName ?? user.UserName!,
            [OpenIddictConstants.Claims.Email] = user.Email!,
            [OpenIddictConstants.Claims.PreferredUsername] = user.UserName!,
            ["roles"] = roles,
            [PlatformClaims.DISPLAY_NAME] = user.DisplayName ?? string.Empty,
        };

        return Results.Ok(claims);
    }
}
