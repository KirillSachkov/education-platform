using System.Net;
using System.Net.Http.Json;
using AuthService.Contracts;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using AuthService.Domain;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class UpdateMyAccountInfoTests : IntegrationTestsBase
{
    public UpdateMyAccountInfoTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task UpdateMyAccountInfo_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("newuser", null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_WithNewUsername_ShouldReturnOk()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("new-username", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify username actually persisted
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("new-username", user!.UserName);

        UserUsernameUpdated message =
            Assert.Single(OutboxCollector.OfType<UserUsernameUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Equal("new-username", message.Username);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_WithEmptyUsername_ShouldReturnBadRequest()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new { Username = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_WithInvalidUsername_ShouldReturnBadRequest()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("ab", null)); // too short

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_DisplayNameOnly_ShouldUpdateOnlyDisplayName()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Original Name", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Original Name", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest(null, "New Display Name"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("New Display Name", user!.DisplayName);
        Assert.Equal("alice@test.com", user.UserName); // unchanged

        UserDisplayNameUpdated message =
            Assert.Single(OutboxCollector.OfType<UserDisplayNameUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Equal("New Display Name", message.DisplayName);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_UsernameOnly_ShouldUpdateOnlyUsername()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Original Display", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Original Display", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("new-handle", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("new-handle", user!.UserName);
        Assert.Equal("Original Display", user.DisplayName); // unchanged
    }

    [Fact]
    public async Task UpdateMyAccountInfo_EmptyBody_ShouldReturnOkAsNoOp()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest(null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("Alice", user!.DisplayName);
        Assert.Equal("alice@test.com", user.UserName);
    }
}
