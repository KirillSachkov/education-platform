using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Stub for the OpenIddict *client* scheme consumed by `GET /auth/github/oidc-callback`.
/// Simulates a completed GitHub OAuth handshake: a principal whose NameIdentifier is the
/// GitHub provider key plus AuthenticationProperties carrying the `github_flow` state item.
/// Activated via ForwardDefaultSelector (see IntegrationTestsWebFactory) only when the
/// X-Test-GitHub-Flow header is present; otherwise the real OpenIddict handler runs.
/// </summary>
public sealed class TestGitHubOAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestGitHubOAuth";

    /// <summary>Value goes into Properties.Items["github_flow"]; "none" omits the item.</summary>
    public const string FlowHeader = "X-Test-GitHub-Flow";
    public const string NoFlowValue = "none";

    public const string ProviderKeyHeader = "X-Test-GitHub-ProviderKey";
    public const string EmailHeader = "X-Test-GitHub-Email";

    public TestGitHubOAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string flow = Request.Headers[FlowHeader].ToString();
        if (string.IsNullOrEmpty(flow))
            return Task.FromResult(AuthenticateResult.NoResult());

        string providerKey = Request.Headers[ProviderKeyHeader].ToString();
        if (string.IsNullOrEmpty(providerKey))
            providerKey = "gh-test-12345";

        List<Claim> claims = [new Claim(ClaimTypes.NameIdentifier, providerKey)];

        string email = Request.Headers[EmailHeader].ToString();
        if (!string.IsNullOrEmpty(email))
            claims.Add(new Claim(ClaimTypes.Email, email));

        ClaimsIdentity identity = new(claims, SchemeName);
        AuthenticationProperties properties = new();
        if (!string.Equals(flow, NoFlowValue, StringComparison.Ordinal))
            properties.Items["github_flow"] = flow;

        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), properties, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
