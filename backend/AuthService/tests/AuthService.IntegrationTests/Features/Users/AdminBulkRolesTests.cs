using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.Admin;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class AdminBulkRolesTests : IntegrationTestsBase
{
    public AdminBulkRolesTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ==========================================================================
    // POST /users/admin/bulk/roles — authorization
    // ==========================================================================

    [Fact]
    public async Task BulkRoles_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/admin/bulk/roles",
            new AdminBulkRolesRequest([Guid.NewGuid()], ["platform-moderator"], []));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BulkRoles_WithoutManagePermission_ShouldReturnForbidden()
    {
        // platform-participant has no Users.MANAGE permission
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/admin/bulk/roles",
            new AdminBulkRolesRequest([Guid.NewGuid()], ["platform-moderator"], []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ==========================================================================
    // Role-name whitelist validation (security fix — mirrors AdminSetRolesValidator)
    // ==========================================================================

    [Fact]
    public async Task BulkRoles_InvalidRoleInAdd_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/admin/bulk/roles",
            new AdminBulkRolesRequest([Guid.NewGuid()], ["not-a-real-role"], []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkRoles_InvalidRoleInRemove_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/admin/bulk/roles",
            new AdminBulkRolesRequest([Guid.NewGuid()], [], ["not-a-real-role"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ==========================================================================
    // Positive case — valid PlatformRoles value is accepted and applied
    // ==========================================================================

    [Fact]
    public async Task BulkRoles_ValidRole_ShouldAddRoleSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        // Ensure target role exists in the Identity roles table
        await SeedRolesAsync("platform-moderator");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/admin/bulk/roles",
            new AdminBulkRolesRequest([userId], ["platform-moderator"], []));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AdminBulkActionResponse body = await ReadResultAsync(response);
        Assert.Contains(userId, body.Succeeded);
        Assert.Empty(body.Failed);

        // Verify the role was actually granted
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        IList<string> roles = await userManager.GetRolesAsync(user!);
        Assert.Contains("platform-moderator", roles);
    }

    [Fact]
    public async Task BulkRoles_SuccessfulRoleChange_ShouldRevokeAllUserTokens()
    {
        OidcTestHelper oidc = new(Factory);
        await oidc.SeedOpenIddictConfigAsync();

        const string targetEmail = "bulk-role-revoke@test.com";
        const string targetPassword = "TargetPass123!";
        Guid targetUserId = Guid.NewGuid();

        await SeedRolesAsync("platform-participant", "platform-moderator");
        await SeedUserWithPasswordAsync(
            targetUserId,
            targetPassword,
            "BulkRoleTarget",
            targetEmail,
            "platform-participant");

        await oidc.LoginAsync(targetEmail, targetPassword);
        OidcTokenResponse tokenResponse = await oidc.ExecuteAuthorizationCodeFlowAsync(
            "test-client",
            "test-secret",
            "openid email roles offline_access platform");
        Assert.True(tokenResponse.IsSuccess, $"Token generation failed: {tokenResponse.RawResponse}");

        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "bulk-role-admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "bulk-role-admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/admin/bulk/roles",
            new AdminBulkRolesRequest([targetUserId], ["platform-moderator"], []));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOpenIddictTokenManager tokenManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        List<string?> tokenStatuses = [];
        await foreach (object token in tokenManager.FindBySubjectAsync(targetUserId.ToString()))
        {
            tokenStatuses.Add(await tokenManager.GetStatusAsync(token));
        }

        Assert.NotEmpty(tokenStatuses);
        Assert.All(tokenStatuses, status =>
            Assert.Equal(OpenIddictConstants.Statuses.Revoked, status));
    }

    [Fact]
    public async Task BulkRoles_PartialRoleChange_ShouldStillRevokeAllUserTokens()
    {
        OidcTestHelper oidc = new(Factory);
        await oidc.SeedOpenIddictConfigAsync();

        const string targetEmail = "bulk-role-partial@test.com";
        const string targetPassword = "TargetPass123!";
        Guid targetUserId = Guid.NewGuid();

        await SeedRolesAsync("platform-participant", "platform-author");
        await SeedUserWithPasswordAsync(
            targetUserId,
            targetPassword,
            "BulkRolePartialTarget",
            targetEmail,
            "platform-participant",
            "platform-author");

        await oidc.LoginAsync(targetEmail, targetPassword);
        OidcTokenResponse tokenResponse = await oidc.ExecuteAuthorizationCodeFlowAsync(
            "test-client",
            "test-secret",
            "openid email roles offline_access platform");
        Assert.True(tokenResponse.IsSuccess, $"Token generation failed: {tokenResponse.RawResponse}");

        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "bulk-partial-admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "bulk-partial-admin@test.com", "platform-admin");

        // Remove succeeds first; adding platform-author then fails because the account already
        // has that role, producing a partial mutation that must still invalidate old claims.
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/admin/bulk/roles",
            new AdminBulkRolesRequest(
                [targetUserId],
                ["platform-author"],
                ["platform-participant"]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IOpenIddictTokenManager tokenManager =
            scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        List<string?> tokenStatuses = [];
        await foreach (object token in tokenManager.FindBySubjectAsync(targetUserId.ToString()))
            tokenStatuses.Add(await tokenManager.GetStatusAsync(token));

        Assert.NotEmpty(tokenStatuses);
        Assert.All(tokenStatuses, status =>
            Assert.Equal(OpenIddictConstants.Statuses.Revoked, status));
    }

    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static async Task<AdminBulkActionResponse> ReadResultAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        // Framework wraps success payloads as { "result": ... }.
        JsonElement body = root.ValueKind == JsonValueKind.Object
                           && root.TryGetProperty("result", out JsonElement wrapped)
            ? wrapped
            : root;

        return body.Deserialize<AdminBulkActionResponse>(_jsonOptions)!;
    }
}
