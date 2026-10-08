using System.Security.Claims;
using AuthService.Core.Options;
using AuthService.Core.Services;
using Framework.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using OpenIddict.Client.AspNetCore;
using PlatformAuth.Authorization;
using HttpContext = Microsoft.AspNetCore.Http.HttpContext;
using HttpMethods = Microsoft.AspNetCore.Http.HttpMethods;
using Results = Microsoft.AspNetCore.Http.Results;

namespace AuthService.Core.Features.Auth.GitHub;

public sealed class GitHubCallbackEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapMethods(
                "/auth/github/oidc-callback",
                [HttpMethods.Get, HttpMethods.Post],
                async (HttpContext httpContext, [FromServices] GitHubCallbackHandler handler)
                    => await handler.HandleAsync(httpContext))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("github");
    }
}

public sealed class GitHubCallbackHandler(
    IOptions<AuthServiceOptions> authOptions,
    GitHubLinkHandler linkHandler,
    GitHubSyncHandler syncHandler,
    ILogger<AuthAudit> auditLogger)
{
    private readonly string _frontendBaseUrl = authOptions.Value.FrontendBaseUrl;

    public async Task<IResult> HandleAsync(HttpContext httpContext)
    {
        AuthenticateResult authResult = await httpContext.AuthenticateAsync(
            OpenIddictClientAspNetCoreDefaults.AuthenticationScheme);

        if (authResult is not { Succeeded: true, Principal.Identity.IsAuthenticated: true })
            return Results.Redirect($"{_frontendBaseUrl}/login?error=github_auth_failed");

        string providerKey = authResult.Principal
            .FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

        if (string.IsNullOrEmpty(providerKey))
            return Results.Redirect($"{_frontendBaseUrl}/login?error=github_auth_failed");

        string? flow = null;
        authResult.Properties?.Items.TryGetValue(GitHubRoutes.FLOW_KEY, out flow);

        return flow switch
        {
            GitHubRoutes.FLOW_LINK => await linkHandler.HandleAsync(authResult, providerKey, httpContext.RequestAborted),
            GitHubRoutes.FLOW_SYNC => await syncHandler.HandleAsync(authResult, providerKey, httpContext.RequestAborted),
            // GitHub as a LOGIN method is disabled (#696): the legacy "login" flow and any
            // unknown flow land on the login page with the contract error code — no user is
            // created, nobody is signed in.
            _ => RedirectLoginDisabled(flow),
        };
    }

    private IResult RedirectLoginDisabled(string? flow)
    {
        auditLogger.LogGitHubDisabledLoginAttempt($"callback:{flow ?? "missing-flow"}");
        return Results.Redirect($"{_frontendBaseUrl}{GitHubRoutes.LOGIN_DISABLED_REDIRECT}");
    }
}
