using System.Net;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features.Connect;

[Collection(nameof(IntegrationTestFixture))]
public class ClientCredentialsFlowTests : IntegrationTestsBase
{
    private const string S2S_CLIENT_ID = "test-s2s";
    private const string S2S_CLIENT_SECRET = "test-s2s-secret";
    private const string FRONTEND_CLIENT_ID = "test-client";
    private const string FRONTEND_CLIENT_SECRET = "test-secret";
    private const string ADMIN_CLIENT_ID = "test-mcp-admin";
    private const string ADMIN_CLIENT_SECRET = "test-mcp-admin-secret";

    private readonly OidcTestHelper _oidc;

    public ClientCredentialsFlowTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        _oidc = new OidcTestHelper(factory);
    }

    private async Task EnsureSeeded() => await _oidc.SeedOpenIddictConfigAsync();

    // ── Happy path ──────────────────────────────────────────────

    [Fact]
    public async Task ClientCredentials_ValidServiceClient_ShouldReturnAccessToken()
    {
        await EnsureSeeded();

        OidcTokenResponse response = await _oidc.ExecuteClientCredentialsFlowAsync(
            S2S_CLIENT_ID, S2S_CLIENT_SECRET, "openid service");

        Assert.True(response.IsSuccess, $"Expected success, got: {response.RawResponse}");
        Assert.NotNull(response.AccessToken);
        Assert.Equal("Bearer", response.TokenType);
        Assert.True(response.ExpiresIn > 0);
    }

    [Fact]
    public async Task ClientCredentials_ServiceClient_ShouldGetServiceAudienceOnly()
    {
        await EnsureSeeded();

        OidcTokenResponse response = await _oidc.ExecuteClientCredentialsFlowAsync(
            S2S_CLIENT_ID, S2S_CLIENT_SECRET, "openid service");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        IReadOnlyList<string> audiences = jwt.GetAudiences();
        Assert.Contains(S2S_CLIENT_ID, audiences);
        Assert.DoesNotContain(FRONTEND_CLIENT_ID, audiences);
    }

    [Fact]
    public async Task ClientCredentials_ServiceClient_ShouldHaveServiceRole()
    {
        await EnsureSeeded();

        OidcTokenResponse response = await _oidc.ExecuteClientCredentialsFlowAsync(
            S2S_CLIENT_ID, S2S_CLIENT_SECRET, "openid service");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        IReadOnlyList<string> roles = jwt.GetRoles();
        Assert.Contains("platform-service", roles);
    }

    // ── Scope isolation ─────────────────────────────────────────

    [Fact]
    public async Task ClientCredentials_ServiceClient_CannotRequestPlatformScope()
    {
        await EnsureSeeded();

        // Service client should not have permission for "platform" scope
        HttpResponseMessage response = await _oidc.SendClientCredentialsRawAsync(
            S2S_CLIENT_ID, S2S_CLIENT_SECRET, "openid platform");

        // OpenIddict strips unauthorized scopes or returns error
        // If it returns a token, the token should NOT contain "platform" audience
        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            OidcTokenResponse tokenResponse = new()
            {
                IsSuccess = true,
                StatusCode = response.StatusCode,
                AccessToken = System.Text.Json.JsonDocument.Parse(json).RootElement
                    .GetProperty("access_token").GetString(),
            };

            JwtPayload jwt = OidcTestHelper.ParseAccessToken(tokenResponse.AccessToken!);
            IReadOnlyList<string> audiences = jwt.GetAudiences();
            Assert.DoesNotContain(FRONTEND_CLIENT_ID, audiences);
        }
        // If it returns an error, that's also acceptable
    }

    [Fact]
    public async Task ClientCredentials_FrontendClient_CannotUseClientCredentialsGrant()
    {
        await EnsureSeeded();

        // Frontend client only has authorization_code and refresh_token grants
        HttpResponseMessage response = await _oidc.SendClientCredentialsRawAsync(
            FRONTEND_CLIENT_ID, FRONTEND_CLIENT_SECRET, "openid platform");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Error cases ─────────────────────────────────────────────

    [Fact]
    public async Task ClientCredentials_InvalidSecret_ShouldFail()
    {
        await EnsureSeeded();

        HttpResponseMessage response = await _oidc.SendClientCredentialsRawAsync(
            S2S_CLIENT_ID, "wrong-secret", "openid service");

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task ClientCredentials_UnknownClient_ShouldFail()
    {
        await EnsureSeeded();

        HttpResponseMessage response = await _oidc.SendClientCredentialsRawAsync(
            "nonexistent-client", "secret", "openid service");

        Assert.False(response.IsSuccessStatusCode);
    }

    // ── Admin (MCP) client ──────────────────────────────────────

    [Fact]
    public async Task ClientCredentials_AdminClient_ShouldIssuePlatformScopedToken()
    {
        await EnsureSeeded();

        OidcTokenResponse response = await _oidc.ExecuteClientCredentialsFlowAsync(
            ADMIN_CLIENT_ID, ADMIN_CLIENT_SECRET, "openid platform");

        Assert.True(response.IsSuccess, $"Expected success, got: {response.RawResponse}");
        Assert.NotNull(response.AccessToken);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);
        Assert.Contains(FRONTEND_CLIENT_ID, jwt.GetAudiences());
    }

    [Fact]
    public async Task ClientCredentials_AdminClient_ShouldHaveAdminRole()
    {
        await EnsureSeeded();

        OidcTokenResponse response = await _oidc.ExecuteClientCredentialsFlowAsync(
            ADMIN_CLIENT_ID, ADMIN_CLIENT_SECRET, "openid platform");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        IReadOnlyList<string> roles = jwt.GetRoles();
        Assert.Contains("platform-admin", roles);
        Assert.DoesNotContain("platform-service", roles);
    }

    [Fact]
    public async Task ClientCredentials_AdminClient_CannotRequestServiceScope()
    {
        await EnsureSeeded();

        HttpResponseMessage response = await _oidc.SendClientCredentialsRawAsync(
            ADMIN_CLIENT_ID, ADMIN_CLIENT_SECRET, "openid service");

        if (response.IsSuccessStatusCode)
        {
            string json = await response.Content.ReadAsStringAsync();
            string? accessToken = System.Text.Json.JsonDocument.Parse(json).RootElement
                .GetProperty("access_token").GetString();

            JwtPayload jwt = OidcTestHelper.ParseAccessToken(accessToken!);
            Assert.DoesNotContain(S2S_CLIENT_ID, jwt.GetAudiences());
        }
    }
}
