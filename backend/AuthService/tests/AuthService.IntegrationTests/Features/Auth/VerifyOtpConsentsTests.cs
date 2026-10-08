using System.Net;
using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using AuthService.Infrastructure.Postgres;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;

namespace AuthService.IntegrationTests.Features.Auth;

/// <summary>
///     Тесты persistence согласий пользователя при OTP-регистрации (#49).
///     Проверяют что:
///     - 2 обязательных consent (OFFER + PERSONAL_DATA) персистятся всегда
///     - MARKETING персистится только если пользователь поставил галочку
///     - Без обязательного consent — регистрация не проходит, account не создаётся
///     - При login существующего пользователя consents не дублируются
/// </summary>
[Collection(nameof(IntegrationTestFixture))]
public class VerifyOtpConsentsTests : IntegrationTestsBase
{
    public VerifyOtpConsentsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task NewUser_WithAllConsents_PersistsTwoMandatoryPlusMarketing()
    {
        const string email = "consent-all@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new
            {
                email,
                code,
                consentOfferAccepted = true,
                consentPersonalDataAccepted = true,
                consentMarketingAccepted = true,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Guid userId = await GetUserIdByEmailAsync(email);
        IReadOnlyList<UserConsent> consents = await GetUserConsentsAsync(userId);

        Assert.Equal(3, consents.Count);
        Assert.Contains(consents, c => c.ConsentType == ConsentType.OFFER);
        Assert.Contains(consents, c => c.ConsentType == ConsentType.PERSONAL_DATA);
        Assert.Contains(consents, c => c.ConsentType == ConsentType.MARKETING);
        Assert.All(consents, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.IpAddress));
            Assert.False(string.IsNullOrEmpty(c.UserAgent));
            Assert.False(string.IsNullOrEmpty(c.DocumentVersion));
        });
    }

    [Fact]
    public async Task NewUser_WithoutMarketing_PersistsOnlyMandatory()
    {
        const string email = "consent-no-marketing@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new
            {
                email,
                code,
                consentOfferAccepted = true,
                consentPersonalDataAccepted = true,
                consentMarketingAccepted = false,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Guid userId = await GetUserIdByEmailAsync(email);
        IReadOnlyList<UserConsent> consents = await GetUserConsentsAsync(userId);

        Assert.Equal(2, consents.Count);
        Assert.DoesNotContain(consents, c => c.ConsentType == ConsentType.MARKETING);
    }

    [Fact]
    public async Task NewUser_WithoutOfferConsent_ReturnsBadRequest_NoUserCreated()
    {
        const string email = "consent-missing-offer@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new
            {
                email,
                code,
                consentOfferAccepted = false,
                consentPersonalDataAccepted = true,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Account? user = await FindUserByEmailAsync(email);
        Assert.Null(user);
    }

    [Fact]
    public async Task NewUser_WithoutPersonalDataConsent_ReturnsBadRequest_NoUserCreated()
    {
        const string email = "consent-missing-pd@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);
        string code = await SendOtpAndGetCodeAsync(email);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new
            {
                email,
                code,
                consentOfferAccepted = true,
                consentPersonalDataAccepted = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Account? user = await FindUserByEmailAsync(email);
        Assert.Null(user);
    }

    [Fact]
    public async Task ExistingUser_LoginViaOtp_DoesNotPersistAdditionalConsents()
    {
        const string email = "consent-existing@test.com";
        await SeedRolesAsync(PlatformRoles.PARTICIPANT);

        // Step 1: register with consents
        string code1 = await SendOtpAndGetCodeAsync(email);
        await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new
            {
                email,
                code = code1,
                consentOfferAccepted = true,
                consentPersonalDataAccepted = true,
                consentMarketingAccepted = false,
            });

        Guid userId = await GetUserIdByEmailAsync(email);
        IReadOnlyList<UserConsent> consentsAfterRegister = await GetUserConsentsAsync(userId);
        Assert.Equal(2, consentsAfterRegister.Count);

        // Step 2: existing user logs in again — no consent flags
        string code2 = await SendOtpAndGetCodeAsync(email);
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/otp/verify",
            new { email, code = code2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<UserConsent> consentsAfterLogin = await GetUserConsentsAsync(userId);
        Assert.Equal(2, consentsAfterLogin.Count);  // unchanged
    }

    // ── Helpers ─────────────────────────────────────────────────────

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

    private async Task<Guid> GetUserIdByEmailAsync(string email)
    {
        Account? user = await FindUserByEmailAsync(email);
        Assert.NotNull(user);
        return user.Id;
    }

    private async Task<IReadOnlyList<UserConsent>> GetUserConsentsAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        AuthDbContext db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await db.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .ToListAsync();
    }
}
