using System.Net;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Features.Auth;

/// <summary>
/// GitHub as a LOGIN method is disabled (#696/#698 — RF law). These tests pin the contract:
/// the login endpoint and the legacy login-flow callback redirect to
/// /login?error=github-login-disabled, create no user and sign nobody in,
/// while the link/sync flows keep dispatching to their handlers.
/// </summary>
[Collection(nameof(IntegrationTestFixture))]
public class GitHubLoginDisabledTests : IntegrationTestsBase
{
    private const string LOGIN_DISABLED_ERROR = "/login?error=github-login-disabled";
    private const string CALLBACK_PATH = "/auth/github/oidc-callback";

    private readonly HttpClient _noRedirectClient;

    public GitHubLoginDisabledTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        _noRedirectClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
    }

    // ── GET /auth/github/login ──────────────────────────────────

    [Fact]
    public async Task GitHubLogin_ShouldRedirectToLoginDisabledError_WithoutOAuthChallenge()
    {
        HttpResponseMessage response = await _noRedirectClient.GetAsync("/auth/github/login");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        string location = response.Headers.Location!.ToString();
        Assert.Contains(LOGIN_DISABLED_ERROR, location, StringComparison.Ordinal);
        // No OAuth challenge — the browser must never be sent to GitHub.
        Assert.DoesNotContain("github.com", location, StringComparison.OrdinalIgnoreCase);
    }

    // ── Callback: legacy login flow ─────────────────────────────

    [Fact]
    public async Task GitHubCallback_LoginFlow_ShouldRedirectToDisabledError_AndCreateNoUser()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, "login");
        request.Headers.Add(TestGitHubOAuthHandler.EmailHeader, "gh-user@test.com");

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            LOGIN_DISABLED_ERROR,
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        // Signs nobody in — no Identity cookie must be issued.
        bool hasIdentityCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasIdentityCookie);

        // Creates no user — the registration path is gone.
        int userCount = await ExecuteInDb(db => db.Users.CountAsync());
        Assert.Equal(0, userCount);
    }

    [Fact]
    public async Task GitHubCallback_LoginFlow_ExistingUserWithGitHubLogin_ShouldNotSignIn()
    {
        // Even a user who previously logged in via GitHub must not be signed in through
        // the legacy login flow (the ExternalLoginSignInAsync path is removed).
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "OldGitHubber", "old-gh@test.com", "platform-participant");
        string providerKey = $"gh-{userId}";
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            Account? user = await userManager.FindByIdAsync(userId.ToString());
            await userManager.AddLoginAsync(user!, new UserLoginInfo("GitHub", providerKey, "GitHub"));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, "login");
        request.Headers.Add(TestGitHubOAuthHandler.ProviderKeyHeader, providerKey);

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            LOGIN_DISABLED_ERROR,
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        bool hasIdentityCookie = response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(c => c.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasIdentityCookie);
    }

    // ── Callback: unknown / missing flow ────────────────────────

    [Fact]
    public async Task GitHubCallback_UnknownFlow_ShouldRedirectToDisabledError()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, "bogus-flow");

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            LOGIN_DISABLED_ERROR,
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubCallback_MissingFlowItem_ShouldRedirectToDisabledError()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, TestGitHubOAuthHandler.NoFlowValue);

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            LOGIN_DISABLED_ERROR,
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    // ── Callback: link / sync flows still dispatch ──────────────

    [Fact]
    public async Task GitHubCallback_LinkFlow_ShouldStillDispatchToLinkHandler()
    {
        // No github_link_user_id item → the link handler's own failure redirect proves
        // the FLOW_LINK dispatch is intact (not swallowed by the disabled-login branch).
        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, "link");

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "/settings/integrations?account=github-link-failed",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubCallback_SyncFlow_ShouldStillDispatchToSyncHandler()
    {
        // No github_link_user_id item → the sync handler's own error redirect proves
        // the FLOW_SYNC dispatch is intact.
        using var request = new HttpRequestMessage(HttpMethod.Get, CALLBACK_PATH);
        request.Headers.Add(TestGitHubOAuthHandler.FlowHeader, "sync");

        HttpResponseMessage response = await _noRedirectClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains(
            "/settings?sync=error",
            response.Headers.Location!.ToString(),
            StringComparison.Ordinal);
    }

    // ── GET /auth/github/sync-courses without Identity ──────────

    [Fact]
    public async Task GitHubSyncCourses_WithoutIdentity_ShouldRedirectToLogin_NotChallenge()
    {
        HttpResponseMessage response = await _noRedirectClient.GetAsync("/auth/github/sync-courses");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        string location = response.Headers.Location!.ToString();
        // callbackUrl brings the user back to the integrations page after email re-login.
        Assert.Contains(
            "/login?callbackUrl=%2Fsettings%2Fintegrations",
            location,
            StringComparison.Ordinal);
        Assert.DoesNotContain("github.com", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitHubSyncCourses_WithIdentity_ShouldChallengeGitHubOAuth()
    {
        // The authenticated branch must still start a real GitHub OAuth challenge for the
        // SYNC flow — only the LOGIN flow is disabled (#696).
        Guid userId = Guid.NewGuid();
        _noRedirectClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                TestAuthHandler.SchemeName,
                $"{userId}|Sync User|sync-user@test.com|platform-participant");

        HttpResponseMessage response = await _noRedirectClient.GetAsync("/auth/github/sync-courses");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        string location = response.Headers.Location!.ToString();
        Assert.Contains("github.com", location, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client_id=test-github-client-id", location, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "error=github-login-disabled",
            location,
            StringComparison.Ordinal);
    }
}
