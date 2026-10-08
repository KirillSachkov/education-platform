using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Contracts.Admin;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class AdminUsersTests : IntegrationTestsBase
{
    public AdminUsersTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ==========================================================================
    // GET /users/ — list
    // ==========================================================================

    [Fact]
    public async Task GetUsers_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_WithoutViewPermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_ShouldReturnUsersList()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_WithSearchFilter_MatchesDisplayName()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "target-display-name", "target-display@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/?search=target-display-name");

        response.EnsureSuccessStatusCode();
        GetUsersResponseShape page = await ReadUsersResponseAsync(response);
        Assert.Contains(page.Items, i => i.Id == targetId);
    }

    [Fact]
    public async Task GetUsers_WithSearchFilter_MatchesUsername()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Target", "target-username@test.com", "platform-participant");
        await SetUsernameAsync(targetId, "target_username_unique");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/?search=target_username_unique");

        response.EnsureSuccessStatusCode();
        GetUsersResponseShape page = await ReadUsersResponseAsync(response);
        Assert.Contains(page.Items, i => i.Id == targetId);
    }

    [Fact]
    public async Task GetUsers_WithSearchFilter_DoesNotMatchEmail()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Email Only", "email-only-needle@test.com", "platform-participant");
        await SetUsernameAsync(targetId, "email_only_user");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/?search=email-only-needle");

        response.EnsureSuccessStatusCode();
        GetUsersResponseShape page = await ReadUsersResponseAsync(response);
        Assert.DoesNotContain(page.Items, i => i.Id == targetId);
    }

    [Fact]
    public async Task GetUsers_WithSearchFilter_MatchesTelegramHandle()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Дмитрий", "tg-target@test.com", "platform-participant");
        await LinkTelegramAsync(targetId, telegramUserId: 555_001L, handle: "tg_unique_handle");

        // #575/#611 — поиск матчит telegram-ник (provider_display_name), не только username/display_name.
        HttpResponseMessage response = await HttpClient.GetAsync("/users/?search=tg_unique_handle");

        response.EnsureSuccessStatusCode();
        GetUsersResponseShape page = await ReadUsersResponseAsync(response);
        Assert.Contains(page.Items, i => i.Id == targetId);
    }

    [Fact]
    public async Task GetUsers_SearchLongerThanLimit_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin-search@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.GetAsync(
            $"/users/?search={new string('a', 101)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ExportUsersCsv_WithSearchFilter_DoesNotMatchEmail()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Csv Email Only", "csv-email-needle@test.com", "platform-participant");
        await SetUsernameAsync(targetId, "csv_email_user");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/export.csv?search=csv-email-needle");

        response.EnsureSuccessStatusCode();
        string csv = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(targetId.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("csv-email-needle@test.com", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportUsersCsv_WithSearchFilter_MatchesTelegramHandle()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Csv Telegram", "csv-tg-target@test.com", "platform-participant");
        await LinkTelegramAsync(targetId, telegramUserId: 555_003L, handle: "csv_tg_unique");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/export.csv?search=csv_tg_unique");

        response.EnsureSuccessStatusCode();
        string csv = await response.Content.ReadAsStringAsync();
        Assert.Contains(targetId.ToString(), csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("csv-tg-target@test.com", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+SUM(1,1)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1,1)")]
    [InlineData(" \t=1+1")]
    public async Task ExportUsersCsv_FormulaLikeDisplayName_ShouldBeEscaped(string displayName)
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin-csv@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin-csv@test.com", "platform-admin");

        await SeedUserAsync(
            Guid.NewGuid(), displayName, "csv-formula@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/admin/export.csv");

        response.EnsureSuccessStatusCode();
        string csv = await response.Content.ReadAsStringAsync();
        string escapedValue = displayName.Contains(',', StringComparison.Ordinal)
            ? $"\"'{displayName}\""
            : $"'{displayName}";
        Assert.Contains(escapedValue, csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchInternalUsers_DoesNotMatchEmail()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Email Only Internal", "internal-email-needle@test.com", "platform-participant");
        await SetUsernameAsync(targetId, "internal_email_user");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/users/search",
            new InternalUsersSearchRequest("internal-email-needle", 10));

        response.EnsureSuccessStatusCode();
        IReadOnlyList<AuthUserLookupDto> users =
            await ReadWrappedResultAsync<IReadOnlyList<AuthUserLookupDto>>(response);
        Assert.DoesNotContain(users, u => u.UserId == targetId);
    }

    [Fact]
    public async Task SearchInternalUsers_MatchesTelegramHandle()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid targetId = Guid.NewGuid();
        await SeedUserAsync(targetId, "Telegram Internal", "internal-tg@test.com", "platform-participant");
        await LinkTelegramAsync(targetId, telegramUserId: 555_002L, handle: "internal_tg_unique");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/users/search",
            new InternalUsersSearchRequest("internal_tg_unique", 10));

        response.EnsureSuccessStatusCode();
        IReadOnlyList<AuthUserLookupDto> users =
            await ReadWrappedResultAsync<IReadOnlyList<AuthUserLookupDto>>(response);
        Assert.Contains(users, u => u.UserId == targetId);
    }

    [Fact]
    public async Task GetUsers_WithRoleFilter_ShouldFilterByRole()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        await SeedUserAsync(Guid.NewGuid(), "Student", "student-role@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/?role=platform-participant");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsModerator_ShouldReturnUsersList()
    {
        Guid modId = Guid.NewGuid();
        await SeedUserAsync(modId, "Moderator", "mod@test.com", "platform-moderator");
        AuthorizeAs(modId, "Moderator", "mod@test.com", "platform-moderator");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_WithCursor_PaginatesThroughAllItems()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // Seed 3 more users so the list has 4 total (admin + 3). Paginate through size=2.
        // SeedUserAsync stamps CreatedAt = DateTime.UtcNow; small delays guarantee distinct ts.
        Guid first = Guid.NewGuid();
        await SeedUserAsync(first, "First", "first-cursor@test.com", "platform-participant");
        await Task.Delay(10);
        Guid second = Guid.NewGuid();
        await SeedUserAsync(second, "Second", "second-cursor@test.com", "platform-participant");
        await Task.Delay(10);
        Guid third = Guid.NewGuid();
        await SeedUserAsync(third, "Third", "third-cursor@test.com", "platform-participant");

        // Page 1 — no cursor. Order is (created_at DESC, id DESC): third, second (admin/first trail).
        HttpResponseMessage firstResponse = await HttpClient.GetAsync("/users/?pageSize=2");
        firstResponse.EnsureSuccessStatusCode();
        GetUsersResponseShape firstPage = await ReadUsersResponseAsync(firstResponse);

        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(2, firstPage.Items.Count);
        List<Guid> firstIds = firstPage.Items.Select(i => i.Id).ToList();
        Assert.Contains(third, firstIds);
        Assert.Contains(second, firstIds);

        // Page 2 — use returned cursor. `page` is ignored; keyset filter returns strictly older.
        HttpResponseMessage secondResponse = await HttpClient.GetAsync(
            $"/users/?pageSize=2&cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        secondResponse.EnsureSuccessStatusCode();
        GetUsersResponseShape secondPage = await ReadUsersResponseAsync(secondResponse);

        List<Guid> secondIds = secondPage.Items.Select(i => i.Id).ToList();
        Assert.DoesNotContain(third, secondIds);
        Assert.DoesNotContain(second, secondIds);
        Assert.Contains(first, secondIds);
        Assert.Contains(adminId, secondIds);
    }

    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private static async Task<GetUsersResponseShape> ReadUsersResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        // Framework wraps success payloads as { "result": ... }.
        JsonElement body = root.ValueKind == JsonValueKind.Object
                           && root.TryGetProperty("result", out JsonElement wrapped)
            ? wrapped
            : root;

        return body.Deserialize<GetUsersResponseShape>(_jsonOptions)!;
    }

    private sealed record GetUsersResponseShape(
        IReadOnlyList<AdminUserRow> Items,
        string? NextCursor,
        int TotalCount,
        int Page,
        int PageSize,
        int TotalPages);

    private static async Task<T> ReadWrappedResultAsync<T>(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement body = root.ValueKind == JsonValueKind.Object
                           && root.TryGetProperty("result", out JsonElement wrapped)
            ? wrapped
            : root;

        return body.Deserialize<T>(_jsonOptions)!;
    }

    private sealed record AdminUserRow(Guid Id, string? UserName, string? Email);

    // ==========================================================================
    // GET /users/{userId} — detail
    // ==========================================================================

    [Fact]
    public async Task GetUserDetail_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserDetail_WithoutViewPermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUserDetail_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserDetail_ExistingUser_ShouldReturnDetail()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId, "Student", "student@test.com", ["platform-participant"]);

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ==========================================================================
    // POST /users/ — create
    // ==========================================================================

    [Fact]
    public async Task CreateUser_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "newuser", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithoutManagePermission_ShouldReturnForbidden()
    {
        // platform-participant has no Users.MANAGE permission
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "newuser", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_AsAdmin_ShouldCreateUserSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        // Ensure target role exists
        await SeedRolesAsync("platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("created@test.com", "createduser", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user and profile were created
        bool profileExists = await ExecuteInDb(async db =>
            await db.UserProfiles.AnyAsync(p => true));

        Assert.True(profileExists);

        UserCreated message = Assert.Single(OutboxCollector.OfType<UserCreated>());
        Assert.Equal("createduser", message.Username);
    }

    [Fact]
    public async Task CreateUser_DuplicateEmail_ShouldReturnConflict()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        await SeedUserAsync(Guid.NewGuid(), "Existing", "existing@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("existing@test.com", "newuser", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_InvalidRole_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "newuser", "Password1!", ["invalid-role"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_EmptyRoles_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "newuser", "Password1!", []));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_InvalidEmail_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("not-an-email", "newuser", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ShortPassword_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "newuser", "12", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_ShortUsername_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync("/users/",
            new AdminCreateUserRequest("new@test.com", "ab", "Password1!", ["platform-participant"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ==========================================================================
    // PATCH /users/{userId} — update
    // ==========================================================================

    [Fact]
    public async Task UpdateUser_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{Guid.NewGuid()}",
            new AdminUpdateUserRequest("new-username", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_WithoutManagePermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{Guid.NewGuid()}",
            new AdminUpdateUserRequest("new-username", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ShouldUpdateUsername()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{userId}",
            new AdminUpdateUserRequest("new-username", null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify username was updated
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? updatedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.Equal("new-username", updatedUser!.UserName);

        UserUsernameUpdated message =
            Assert.Single(OutboxCollector.OfType<UserUsernameUpdated>());
        Assert.Equal(userId, message.UserId);
        Assert.Equal("new-username", message.Username);
    }

    [Fact]
    public async Task UpdateUser_ShouldUpdateEmail()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{userId}",
            new AdminUpdateUserRequest(null, "newemail@test.com", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_ShouldUpdateEmailConfirmed()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{userId}",
            new AdminUpdateUserRequest(null, null, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? updatedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.False(updatedUser!.EmailConfirmed);
    }

    [Fact]
    public async Task UpdateUser_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{Guid.NewGuid()}",
            new AdminUpdateUserRequest("new-username", null, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateUser_AllFieldsNull_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync($"/users/{Guid.NewGuid()}",
            new AdminUpdateUserRequest(null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ==========================================================================
    // POST /users/{userId}/password — set password
    // ==========================================================================

    [Fact]
    public async Task SetPassword_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/password",
            new AdminSetPasswordRequest("NewPassword1!"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_WithoutManagePermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/password",
            new AdminSetPasswordRequest("NewPassword1!"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_ShouldChangePasswordSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{userId}/password",
            new AdminSetPasswordRequest("NewPassword1!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/password",
            new AdminSetPasswordRequest("NewPassword1!"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_ShortPassword_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/password",
            new AdminSetPasswordRequest("12"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetPassword_ShouldRevokeAllUserTokens()
    {
        // Arrange: seed OpenIddict config, create target user with password, generate real tokens
        OidcTestHelper oidc = new(Factory);
        await oidc.SeedOpenIddictConfigAsync();

        string targetEmail = "revoke-target@test.com";
        string targetPassword = "TargetPass123!";
        Guid targetUserId = Guid.NewGuid();

        await SeedRolesAsync("platform-participant");
        await SeedUserWithPasswordAsync(
            targetUserId, targetPassword, "RevokeTarget", targetEmail, "platform-participant");

        // Login as the target user and perform an OIDC authorization code flow to create tokens
        await oidc.LoginAsync(targetEmail, targetPassword);
        OidcTokenResponse tokenResponse = await oidc.ExecuteAuthorizationCodeFlowAsync(
            "test-client", "test-secret", "openid email roles offline_access platform");

        Assert.True(tokenResponse.IsSuccess, $"Token generation failed: {tokenResponse.RawResponse}");
        Assert.NotNull(tokenResponse.RefreshToken);

        // Verify tokens exist for the target user before the admin action
        await using (AsyncServiceScope scopeBefore = Services.CreateAsyncScope())
        {
            IOpenIddictTokenManager tokenManager =
                scopeBefore.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

            int tokenCountBefore = 0;
            await foreach (object _ in tokenManager.FindBySubjectAsync(targetUserId.ToString()))
                tokenCountBefore++;

            Assert.True(tokenCountBefore > 0, "Expected at least one token before admin set password");
        }

        // Act: call admin set password as admin
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin-revoke@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin-revoke@test.com", "platform-admin");

        HttpResponseMessage setPasswordResponse = await HttpClient.PostAsJsonAsync(
            $"/users/{targetUserId}/password",
            new AdminSetPasswordRequest("NewAdminPass123!"));

        Assert.Equal(HttpStatusCode.OK, setPasswordResponse.StatusCode);

        // Assert: all tokens for the target user should be revoked
        await using AsyncServiceScope scopeAfter = Services.CreateAsyncScope();
        IOpenIddictTokenManager tokenManagerAfter =
            scopeAfter.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();

        List<string?> tokenStatuses = [];
        await foreach (object token in tokenManagerAfter.FindBySubjectAsync(targetUserId.ToString()))
        {
            string? status = await tokenManagerAfter.GetStatusAsync(token);
            tokenStatuses.Add(status);
        }

        Assert.NotEmpty(tokenStatuses);
        Assert.All(tokenStatuses, status =>
            Assert.Equal(OpenIddict.Abstractions.OpenIddictConstants.Statuses.Revoked, status));
    }

    // ==========================================================================
    // PUT /users/{userId}/roles — set roles
    // ==========================================================================

    [Fact]
    public async Task SetRoles_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{Guid.NewGuid()}/roles",
            new AdminSetRolesRequest(["platform-participant"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetRoles_WithoutManagePermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{Guid.NewGuid()}/roles",
            new AdminSetRolesRequest(["platform-participant"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetRoles_ShouldUpdateRolesSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        // Ensure target role exists
        await SeedRolesAsync("platform-author");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{userId}/roles",
            new AdminSetRolesRequest(["platform-author"]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify roles were updated
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        IList<string> newRoles = await userManager.GetRolesAsync(user!);
        Assert.Contains("platform-author", newRoles);
        Assert.DoesNotContain("platform-participant", newRoles);
    }

    [Fact]
    public async Task SetRoles_RemoveOwnAdmin_ShouldReturnBadRequest()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        await SeedRolesAsync("platform-participant");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{adminId}/roles",
            new AdminSetRolesRequest(["platform-participant"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetRoles_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        await SeedRolesAsync("platform-participant");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{Guid.NewGuid()}/roles",
            new AdminSetRolesRequest(["platform-participant"]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetRoles_InvalidRole_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{Guid.NewGuid()}/roles",
            new AdminSetRolesRequest(["invalid-role"]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetRoles_EmptyRoles_ShouldReturnBadRequest()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PutAsJsonAsync($"/users/{Guid.NewGuid()}/roles",
            new AdminSetRolesRequest([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ==========================================================================
    // POST /users/{userId}/lockout — lock/unlock
    // ==========================================================================

    [Fact]
    public async Task SetLockout_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/lockout",
            new AdminSetLockoutRequest(true, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetLockout_WithoutManagePermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/lockout",
            new AdminSetLockoutRequest(true, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetLockout_ShouldLockUserSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{userId}/lockout",
            new AdminSetLockoutRequest(true, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user is locked
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.True(await userManager.IsLockedOutAsync(user!));
    }

    [Fact]
    public async Task SetLockout_ShouldUnlockUserSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student@test.com", "platform-participant");

        // Lock first
        await HttpClient.PostAsJsonAsync($"/users/{userId}/lockout",
            new AdminSetLockoutRequest(true, null));

        // Then unlock
        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{userId}/lockout",
            new AdminSetLockoutRequest(false, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        Assert.False(await userManager.IsLockedOutAsync(user!));
    }

    [Fact]
    public async Task SetLockout_Self_ShouldReturnBadRequest()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{adminId}/lockout",
            new AdminSetLockoutRequest(true, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetLockout_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync($"/users/{Guid.NewGuid()}/lockout",
            new AdminSetLockoutRequest(true, null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ==========================================================================
    // DELETE /users/{userId} — delete
    // ==========================================================================

    [Fact]
    public async Task DeleteUser_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.DeleteAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_WithoutManagePermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.DeleteAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_ShouldDeleteSuccessfully()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId, "Student", "student@test.com", ["platform-participant"]);

        HttpResponseMessage response = await HttpClient.DeleteAsync($"/users/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user is deleted
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? deletedUser = await userManager.FindByIdAsync(userId.ToString());
        Assert.Null(deletedUser);

        // Verify profile is cascade-deleted
        bool profileExists = await ExecuteInDb(async db =>
            await db.UserProfiles.AnyAsync(p => p.Id == userId));
        Assert.False(profileExists);
    }

    [Fact]
    public async Task DeleteUser_Self_ShouldReturnBadRequest()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.DeleteAsync($"/users/{adminId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_NonExistentUser_ShouldReturnNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Admin", "admin@test.com", "platform-admin");

        HttpResponseMessage response = await HttpClient.DeleteAsync($"/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>#575 — сидит Telegram-логин в user_logins (provider_display_name = handle).</summary>
    private async Task LinkTelegramAsync(Guid userId, long telegramUserId, string handle)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        IdentityResult result = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(
                TelegramProviderConstants.PROVIDER_NAME,
                telegramUserId.ToString(CultureInfo.InvariantCulture),
                handle));
        Assert.True(result.Succeeded);
    }

    private async Task SetUsernameAsync(Guid userId, string username)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;

        IdentityResult result = await userManager.SetUserNameAsync(user, username);
        Assert.True(result.Succeeded);
    }
}
