using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class DisplayNameAndUsernameTests : IntegrationTestsBase
{
    public DisplayNameAndUsernameTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task UpdateMyAccountInfo_WithDisplayName_ShouldPersistDisplayName()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);
        AuthorizeAs(userId, "Alice", "alice@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("testuser", "Test User"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());

        Assert.NotNull(user);
        Assert.Equal("Test User", user!.DisplayName);
    }

    [Fact]
    public async Task UpdateMyAccountInfo_WithEmptyDisplayName_ShouldReturnBadRequest()
    {
        // DisplayName is required after the onboarding rework — it can no longer be cleared
        // via PATCH /users/me/account. Sending an empty string must fail validation rather
        // than silently nulling the field (which would force the user back through onboarding).
        Guid userId = Guid.CreateVersion7();
        await SeedUserAsync(userId, "Carol", "carol@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        await using (AsyncServiceScope setupScope = Services.CreateAsyncScope())
        {
            UserManager<Account> setupManager =
                setupScope.ServiceProvider.GetRequiredService<UserManager<Account>>();
            Account? existing = await setupManager.FindByIdAsync(userId.ToString());
            existing!.DisplayName = "Carol Display";
            await setupManager.UpdateAsync(existing);
        }

        AuthorizeAs(userId, "Carol", "carol@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("carol-user", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("Carol Display", user!.DisplayName);
    }

    [Fact]
    public async Task GetMyProfile_ShouldReturnUsername()
    {
        Guid userId = Guid.CreateVersion7();
        await SeedUserWithProfileAsync(userId, "Dave", "dave@test.com",
            [PlatformAuth.Authorization.PlatformRoles.PARTICIPANT]);

        AuthorizeAs(userId, "Dave", "dave@test.com",
            PlatformAuth.Authorization.PlatformRoles.PARTICIPANT);

        HttpResponseMessage updateResponse = await HttpClient.PatchAsJsonAsync(
            "/users/me/account",
            new UpdateMyAccountInfoRequest("dave-custom", null));
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetMyProfileResponse? dto = await ReadProfileResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal(userId, dto!.Id);
        Assert.Equal("dave-custom", dto.Username);
    }

    private static async Task<GetMyProfileResponse?> ReadProfileResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out JsonElement wrappedResult))
        {
            return wrappedResult.Deserialize<GetMyProfileResponse>(options);
        }

        return root.Deserialize<GetMyProfileResponse>(options);
    }
}
