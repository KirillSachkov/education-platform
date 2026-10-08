using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;

namespace AuthService.IntegrationTests.Features.Connect;

[Collection(nameof(IntegrationTestFixture))]
public class AuthorizationCodeFlowTests : IntegrationTestsBase
{
    private const string CLIENT_ID = "test-client";
    private const string CLIENT_SECRET = "test-secret";
    private const string S2S_CLIENT_ID = "test-s2s";
    private const string PASSWORD = "TestPass123!";
    private const string EMAIL = "oidc-user@test.com";

    private readonly OidcTestHelper _oidc;

    public AuthorizationCodeFlowTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        _oidc = new OidcTestHelper(factory);
    }

    private async Task SeedAndLogin(string email = EMAIL, params string[] roles)
    {
        await _oidc.SeedOpenIddictConfigAsync();
        await SeedRolesAsync(roles.Length > 0 ? roles : [PlatformRoles.PARTICIPANT]);
        await SeedUserWithPasswordAsync(
            Guid.NewGuid(), PASSWORD, "OIDC User", email,
            roles.Length > 0 ? roles : [PlatformRoles.PARTICIPANT]);
        await _oidc.LoginAsync(email, PASSWORD);
    }

    // ── Happy path ──────────────────────────────────────────────

    [Fact]
    public async Task AuthorizationCode_FullFlow_ShouldReturnTokens()
    {
        await SeedAndLogin();

        OidcTokenResponse response = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles offline_access platform");

        Assert.True(response.IsSuccess, $"Token exchange failed: {response.RawResponse}");
        Assert.NotNull(response.AccessToken);
        Assert.NotNull(response.RefreshToken);
        Assert.Equal("Bearer", response.TokenType);
    }

    [Fact]
    public async Task AuthorizationCode_FrontendClient_ShouldGetPlatformAudienceOnly()
    {
        await SeedAndLogin();

        OidcTokenResponse response = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles platform");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        IReadOnlyList<string> audiences = jwt.GetAudiences();
        Assert.Contains(CLIENT_ID, audiences);
        Assert.DoesNotContain(S2S_CLIENT_ID, audiences);
    }

    [Fact]
    public async Task AuthorizationCode_ShouldIncludeUserClaims()
    {
        await SeedAndLogin();

        OidcTokenResponse response = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles platform");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        Assert.NotNull(jwt.sub);
        Assert.Equal(EMAIL, jwt.email);
        Assert.Contains(PlatformRoles.PARTICIPANT, jwt.GetRoles());
    }

    [Fact]
    public async Task AuthorizationCode_FrontendClient_CannotRequestServiceScope()
    {
        // Frontend client should NOT have permission for "service" scope.
        // OpenIddict rejects the request at middleware level before reaching our code.
        await SeedAndLogin();

        HttpClient rawClient = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        // Login to get cookie on this client
        await rawClient.PostAsJsonAsync("/auth/login", new { email = EMAIL, password = PASSWORD });

        string codeVerifier = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.ASCII.GetBytes(codeVerifier));
        string codeChallenge = Convert.ToBase64String(hash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        HttpResponseMessage response = await rawClient.GetAsync(
            $"/connect/authorize?response_type=code" +
            $"&client_id={CLIENT_ID}" +
            $"&redirect_uri=http://localhost/callback" +
            $"&scope=openid+email+roles+platform+service" +
            $"&code_challenge={codeChallenge}" +
            $"&code_challenge_method=S256");

        // OpenIddict should reject: frontend client doesn't have scp:service
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AuthorizationCode_AdminUser_ShouldIncludeAdminRole()
    {
        string email = "admin-oidc@test.com";
        await SeedAndLogin(email, PlatformRoles.ADMIN, PlatformRoles.PARTICIPANT);

        OidcTokenResponse response = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles platform");

        Assert.True(response.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);

        IReadOnlyList<string> roles = jwt.GetRoles();
        Assert.Contains(PlatformRoles.ADMIN, roles);
        Assert.Contains(PlatformRoles.PARTICIPANT, roles);
    }

    [Fact]
    public async Task AuthorizationCode_LockedUserWithExistingCookie_ShouldBeRejected()
    {
        Guid userId = Guid.NewGuid();
        await _oidc.SeedOpenIddictConfigAsync();
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        await SeedUserWithPasswordAsync(
            userId, PASSWORD, "Locked OIDC User", EMAIL, PlatformRoles.PARTICIPANT);
        await _oidc.LoginAsync(EMAIL, PASSWORD);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            UserManager<Account> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
            IdentityResult result = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(1));
            Assert.True(result.Succeeded);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _oidc.ExecuteAuthorizationCodeFlowAsync(
                CLIENT_ID, CLIENT_SECRET, "openid email roles platform"));
    }

    [Fact]
    public async Task AuthorizationCode_StaleSecurityStampCookie_ShouldBeRejected()
    {
        Guid userId = Guid.NewGuid();
        const string email = "stale-cookie@test.com";
        await _oidc.SeedOpenIddictConfigAsync();
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        await SeedUserWithPasswordAsync(
            userId, PASSWORD, "Stale Cookie User", email, PlatformRoles.PARTICIPANT);
        await _oidc.LoginAsync(email, PASSWORD);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            UserManager<Account> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
            IdentityResult result = await userManager.UpdateSecurityStampAsync(user);
            Assert.True(result.Succeeded);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _oidc.ExecuteAuthorizationCodeFlowAsync(
                CLIENT_ID, CLIENT_SECRET, "openid email roles platform"));
    }

    // ── Refresh token ───────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_ValidToken_ShouldReturnNewAccessToken()
    {
        await SeedAndLogin();

        OidcTokenResponse initial = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles offline_access platform");

        Assert.True(initial.IsSuccess);
        Assert.NotNull(initial.RefreshToken);

        OidcTokenResponse refreshed = await _oidc.RefreshTokenAsync(
            CLIENT_ID, CLIENT_SECRET, initial.RefreshToken!);

        Assert.True(refreshed.IsSuccess, $"Refresh failed: {refreshed.RawResponse}");
        Assert.NotNull(refreshed.AccessToken);

        // Refreshed token should have the same audience
        JwtPayload jwt = OidcTestHelper.ParseAccessToken(refreshed.AccessToken!);
        IReadOnlyList<string> audiences = jwt.GetAudiences();
        Assert.Contains(CLIENT_ID, audiences);
        Assert.DoesNotContain(S2S_CLIENT_ID, audiences);
    }

    [Fact]
    public async Task RefreshToken_PreservesUserClaims()
    {
        await SeedAndLogin();

        OidcTokenResponse initial = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles offline_access platform");

        Assert.True(initial.IsSuccess);

        OidcTokenResponse refreshed = await _oidc.RefreshTokenAsync(
            CLIENT_ID, CLIENT_SECRET, initial.RefreshToken!);

        Assert.True(refreshed.IsSuccess);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(refreshed.AccessToken!);
        Assert.Equal(EMAIL, jwt.email);
        Assert.Contains(PlatformRoles.PARTICIPANT, jwt.GetRoles());
    }

    [Fact]
    public async Task RefreshToken_InvalidToken_ShouldFail()
    {
        await _oidc.SeedOpenIddictConfigAsync();

        OidcTokenResponse response = await _oidc.RefreshTokenAsync(
            CLIENT_ID, CLIENT_SECRET, "invalid-refresh-token");

        Assert.False(response.IsSuccess);
    }

    // ── display_name claim — onboarding signal ──────────────────
    //
    // Regression guard: OpenIddict's `.SetClaim(type, "")` silently drops empty values.
    // The Authorization + Token endpoints emit display_name explicitly via AddClaim so the
    // empty-DisplayName signal survives in id_token/access_token (not just in userinfo,
    // which is built manually). Without it, fresh OTP users get displayName=undefined in
    // the JWT and the onboarding redirect on the frontend becomes timing-dependent.

    [Fact]
    public async Task AuthorizationCode_NullDisplayName_ShouldEmitEmptyDisplayNameClaim()
    {
        string email = "noname-oidc@test.com";
        Guid userId = Guid.NewGuid();
        await _oidc.SeedOpenIddictConfigAsync();
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        await SeedUserWithPasswordAsync(userId, PASSWORD, "Throwaway Name", email, PlatformRoles.PARTICIPANT);
        await SetDisplayNameAsync(userId, null);
        await _oidc.LoginAsync(email, PASSWORD);

        OidcTokenResponse response = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles platform");

        Assert.True(response.IsSuccess, response.RawResponse);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(response.AccessToken!);
        Assert.NotNull(jwt.display_name);
        Assert.Equal(string.Empty, jwt.display_name);
    }

    [Fact]
    public async Task RefreshToken_NullDisplayName_ShouldEmitEmptyDisplayNameClaim()
    {
        string email = "noname-refresh@test.com";
        Guid userId = Guid.NewGuid();
        await _oidc.SeedOpenIddictConfigAsync();
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        await SeedUserWithPasswordAsync(userId, PASSWORD, "Throwaway Name", email, PlatformRoles.PARTICIPANT);
        await SetDisplayNameAsync(userId, null);
        await _oidc.LoginAsync(email, PASSWORD);

        OidcTokenResponse initial = await _oidc.ExecuteAuthorizationCodeFlowAsync(
            CLIENT_ID, CLIENT_SECRET, "openid email roles offline_access platform");
        Assert.True(initial.IsSuccess, initial.RawResponse);

        OidcTokenResponse refreshed = await _oidc.RefreshTokenAsync(
            CLIENT_ID, CLIENT_SECRET, initial.RefreshToken!);
        Assert.True(refreshed.IsSuccess, refreshed.RawResponse);

        JwtPayload jwt = OidcTestHelper.ParseAccessToken(refreshed.AccessToken!);
        Assert.NotNull(jwt.display_name);
        Assert.Equal(string.Empty, jwt.display_name);
    }

    private async Task SetDisplayNameAsync(Guid userId, string? displayName)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        user.DisplayName = displayName;
        IdentityResult result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to update DisplayName: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
