using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class CompleteProfileTests : IntegrationTestsBase
{
    public CompleteProfileTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task CompleteProfile_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest("Иван Иванов"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteProfile_WithValidDisplayName_ShouldPersistAndReturnOk()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Test User", "alice@test.com",
            PlatformRoles.PARTICIPANT);
        await ClearDisplayNameAsync(userId);

        AuthorizeAs(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest("Иван Иванов"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("Иван Иванов", user!.DisplayName);

        UserDisplayNameUpdated message =
            Assert.Single(OutboxCollector.OfType<UserDisplayNameUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Equal("Иван Иванов", message.DisplayName);
    }

    [Fact]
    public async Task CompleteProfile_TrimsWhitespace()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest("  Маша  "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("Маша", user!.DisplayName);
    }

    [Fact]
    public async Task CompleteProfile_WithEmptyDisplayName_ShouldKeepDisplayNameNull()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);
        await ClearDisplayNameAsync(userId);
        AuthorizeAs(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest(""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Null(user!.DisplayName);
    }

    [Fact]
    public async Task CompleteProfile_WithWhitespaceOnly_ShouldKeepDisplayNameNull()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);
        await ClearDisplayNameAsync(userId);
        AuthorizeAs(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest("   "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Null(user!.DisplayName);
    }

    [Fact]
    public async Task CompleteProfile_WithDisplayNameTooLong_ShouldReturnBadRequest()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Test User", "alice@test.com", PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/users/me/profile/complete",
            new CompleteProfileRequest(new string('A', 51)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task ClearDisplayNameAsync(Guid userId)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        user.DisplayName = null;
        IdentityResult result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Failed to clear DisplayName: {string.Join(", ", result.Errors.Select(e => e.Description))}");
    }
}
