using System.Net;
using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.IntegrationTests.Features.Auth;

[Collection(nameof(IntegrationTestFixture))]
public class OtpFlowTests : IntegrationTestsBase
{
    public OtpFlowTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── SendOtp ─────────────────────────────────────────────────

    [Fact]
    public async Task SendOtp_NewEmail_ShouldSendCodeWithoutCreatingUser()
    {
        ClearAuthorization();
        FakeEmailSender emailSender = GetFakeEmailSender();
        emailSender.Clear();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/send", new { email = "newuser@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(emailSender.SentCodes, c => c.Email == "newuser@test.com");

        // No user created — account only created after OTP verification
        Account? user = await FindUserByEmailAsync("newuser@test.com");
        Assert.Null(user);
    }

    [Fact]
    public async Task SendOtp_ExistingUser_ShouldSendCode()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Existing", "existing-otp@test.com", PlatformRoles.PARTICIPANT);
        ClearAuthorization();
        FakeEmailSender emailSender = GetFakeEmailSender();
        emailSender.Clear();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/send", new { email = "existing-otp@test.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(emailSender.SentCodes, c => c.Email == "existing-otp@test.com");
    }

    [Fact]
    public async Task SendOtp_InvalidEmail_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/send", new { email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendOtp_EmptyEmail_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/send", new { email = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── VerifyOtp — new user ────────────────────────────────────

    [Fact]
    public async Task VerifyOtp_NewUser_ShouldCreateAccountAndAuthenticate()
    {
        string email = "verify-new@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Account created with EmailConfirmed = true
        Account? user = await FindUserByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task VerifyOtp_NewUser_ShouldGenerateUsername()
    {
        string email = "john.doe@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        await HttpClient.PostAsJsonAsync("/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });

        Account? user = await FindUserByEmailAsync(email);
        Assert.NotNull(user);
        Assert.StartsWith("john.doe", user.UserName!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerifyOtp_NewUser_ShouldPublishUserCreated()
    {
        const string email = "outbox-user@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Account user = (await FindUserByEmailAsync(email))!;
        UserCreated message = Assert.Single(OutboxCollector.OfType<UserCreated>());
        Assert.Equal(user.Id, message.UserId);
        Assert.Equal(user.UserName, message.Username);
    }

    // ── VerifyOtp — existing user ───────────────────────────────

    [Fact]
    public async Task VerifyOtp_ExistingUser_ShouldAuthenticate()
    {
        string email = "existing-verify@test.com";
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Existing", email, PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UserLoggedIn message = Assert.Single(OutboxCollector.OfType<UserLoggedIn>());
        Assert.Equal(userId, message.UserId);
    }

    [Fact]
    public async Task VerifyOtp_InvalidCode_ShouldReturnBadRequest()
    {
        string email = "bad-code@test.com";
        await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_NoCodeSent_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email = "nobody@test.com", code = "123456" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_LockedExistingAccount_ShouldReturnBadRequest()
    {
        string email = "locked-otp@test.com";
        await SeedUserAsync(Guid.NewGuid(), "Locked", email, PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        await LockUserAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Full flow ───────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_SendAndVerify_ShouldSetIdentityCookie()
    {
        string email = "full-flow@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);

        FakeEmailSender emailSender = GetFakeEmailSender();
        emailSender.Clear();

        // Step 1: Send OTP (no account created)
        HttpResponseMessage sendResponse = await HttpClient.PostAsJsonAsync(
            "/auth/otp/send", new { email });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        string code = emailSender.SentCodes.First(c => c.Email == email).Code;

        // Step 2: Verify OTP (creates account + authenticates)
        HttpResponseMessage verifyResponse = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        Assert.True(
            verifyResponse.Headers.Contains("Set-Cookie"),
            "Expected Set-Cookie header after OTP verification");

        // Verify account created correctly
        Account? user = await FindUserByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task VerifyOtp_CodeConsumed_SecondAttemptFails()
    {
        string email = "one-time@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        // First verify — succeeds
        HttpResponseMessage first = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second verify with same code — fails (consumed)
        HttpResponseMessage second = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify", new { email, code, consentOfferAccepted = true, consentPersonalDataAccepted = true });
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task<string> SendOtpAndGetCodeAsync(string email)
    {
        ClearAuthorization();
        FakeEmailSender emailSender = GetFakeEmailSender();
        emailSender.Clear();

        await HttpClient.PostAsJsonAsync("/auth/otp/send", new { email });

        return emailSender.SentCodes.First(c => c.Email == email).Code;
    }

    private async Task<Account?> FindUserByEmailAsync(string email)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        return await userManager.FindByEmailAsync(email);
    }

    private async Task LockUserAsync(string email)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByEmailAsync(email);

        if (user is not null)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
