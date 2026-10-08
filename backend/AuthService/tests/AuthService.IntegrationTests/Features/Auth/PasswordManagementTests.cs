using System.Net;
using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Features.Auth;

[Collection(nameof(IntegrationTestFixture))]
public class PasswordManagementTests : IntegrationTestsBase
{
    public PasswordManagementTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── SetPassword ─────────────────────────────────────────────

    [Fact]
    public async Task SetPassword_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/set", new { password = "Test1234!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_UserWithoutPassword_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        // SeedUserAsync creates user without password
        await SeedUserAsync(userId, "NoPass", "nopass@test.com", "platform-participant");
        AuthorizeAs(userId, "NoPass", "nopass@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/set", new { password = "NewPass123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_UserAlreadyHasPassword_ShouldReturnConflict()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "Existing123!", "HasPass", "haspass@test.com", "platform-participant");
        AuthorizeAs(userId, "HasPass", "haspass@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/set", new { password = "Another123!" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_EmptyPassword_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "NoPass", "nopass2@test.com", "platform-participant");
        AuthorizeAs(userId, "NoPass", "nopass2@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/set", new { password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── ChangePassword ──────────────────────────────────────────

    [Fact]
    public async Task ChangePassword_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/change", new { currentPassword = "Old123!", newPassword = "New123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_ValidCredentials_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "Current123!", "Changer", "changer@test.com", "platform-participant");
        AuthorizeAs(userId, "Changer", "changer@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/change", new { currentPassword = "Current123!", newPassword = "Updated123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "Correct123!", "Wrong", "wrong@test.com", "platform-participant");
        AuthorizeAs(userId, "Wrong", "wrong@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/change", new { currentPassword = "WrongPassword!", newPassword = "New123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── ForgotPassword ──────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_ExistingUser_ShouldSendResetEmail()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "Pass123!", "Forgot", "forgot@test.com", "platform-participant");
        ClearAuthorization();

        FakeEmailSender emailSender = GetFakeEmailSender();
        emailSender.Clear();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/forgot", new { email = "forgot@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(emailSender.SentResetLinks, r => r.Email == "forgot@test.com");
    }

    [Fact]
    public async Task ForgotPassword_NonExistentUser_ShouldStillReturnOk()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/forgot", new { email = "nonexistent@test.com" });

        // Should return OK to prevent email enumeration
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_InvalidEmail_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/forgot", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── ResetPassword ───────────────────────────────────────────

    [Fact]
    public async Task ResetPassword_ValidToken_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "OldPass123!", "Resetter", "resetter@test.com", "platform-participant");
        ClearAuthorization();

        // Generate a real reset token
        string token = await GeneratePasswordResetTokenAsync(userId);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/reset", new { email = "resetter@test.com", token, newPassword = "NewPass123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "OldPass123!", "BadToken", "badtoken@test.com", "platform-participant");
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/reset", new { email = "badtoken@test.com", token = "invalid-token", newPassword = "NewPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_NonExistentUser_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/password/reset", new { email = "nobody@test.com", token = "some-token", newPassword = "NewPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<string> GeneratePasswordResetTokenAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GeneratePasswordResetTokenAsync(user!);
    }
}
