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
public class LoginTests : IntegrationTestsBase
{
    private const string PASSWORD = "TestPass123!";

    public LoginTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── Login ───────────────────────────────────────────────────

    [Fact]
    public async Task Login_ValidCredentials_ShouldReturnOk()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, PASSWORD, "LoginUser", "login@test.com", PlatformRoles.PARTICIPANT);
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "login@test.com", password = PASSWORD });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UserLoggedIn message = Assert.Single(OutboxCollector.OfType<UserLoggedIn>());
        Assert.Equal(userId, message.UserId);
    }

    [Fact]
    public async Task Login_EmailDiffersFromUsername_ShouldReturnOk()
    {
        const string email = "generated-username@test.com";
        Guid userId = Guid.NewGuid();

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            RoleManager<Role> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

            if (!await roleManager.RoleExistsAsync(PlatformRoles.PARTICIPANT))
                await roleManager.CreateAsync(new Role { Id = Guid.NewGuid(), Name = PlatformRoles.PARTICIPANT });

            var user = new Account
            {
                Id = userId,
                UserName = "generated-username",
                Email = email,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            IdentityResult createResult = await userManager.CreateAsync(user, PASSWORD);
            Assert.True(createResult.Succeeded);
            await userManager.AddToRoleAsync(user, PlatformRoles.PARTICIPANT);
        }

        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login",
            new { email, password = PASSWORD });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ShouldSetIdentityCookie()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, PASSWORD, "CookieUser", "cookie@test.com", PlatformRoles.PARTICIPANT);
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "cookie@test.com", password = PASSWORD });

        Assert.True(
            response.Headers.Contains("Set-Cookie"),
            "Expected Set-Cookie header after login");
    }

    [Fact]
    public async Task Login_InvalidPassword_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, PASSWORD, "BadPwd", "badpwd@test.com", PlatformRoles.PARTICIPANT);
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "badpwd@test.com", password = "WrongPass123!" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_NonExistentUser_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "nobody@test.com", password = PASSWORD });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnconfirmedEmail_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            RoleManager<Role> roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

            if (!await roleManager.RoleExistsAsync(PlatformRoles.PARTICIPANT))
                await roleManager.CreateAsync(new Role { Id = Guid.NewGuid(), Name = PlatformRoles.PARTICIPANT });

            var user = new Account
            {
                Id = userId,
                UserName = "unconfirmed@test.com",
                Email = "unconfirmed@test.com",
                EmailConfirmed = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            await userManager.CreateAsync(user, PASSWORD);
            await userManager.AddToRoleAsync(user, PlatformRoles.PARTICIPANT);
        }

        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "unconfirmed@test.com", password = PASSWORD });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_LockedAccount_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, PASSWORD, "Locked", "locked@test.com", PlatformRoles.PARTICIPANT);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            UserManager<Account> userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            Account? user = await userManager.FindByEmailAsync("locked@test.com");
            await userManager.SetLockoutEnabledAsync(user!, true);
            await userManager.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(1));
        }

        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "locked@test.com", password = PASSWORD });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_EmptyEmail_ShouldReturnBadRequest()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/login", new { email = "", password = PASSWORD });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterEndpoint_ShouldNotExist()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/auth/register", new { email = "test@test.com", password = PASSWORD, username = "test" });

        // Endpoint removed — returns 401 (auth pipeline) or 404 depending on config
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized,
            $"Expected 404 or 401 but got {response.StatusCode}");
    }
}
