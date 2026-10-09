using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.AuthorSpaces;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AuthService.IntegrationTests.Features.AuthorSpaces;

[Collection(nameof(IntegrationTestFixture))]
public class AuthorSpaceTests : IntegrationTestsBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthorSpaceTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── GetMySpace ──────────────────────────────────────────────

    [Fact]
    public async Task GetMySpace_AsAuthor_WithExistingSpace_ShouldReturn()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Author One", "author1@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "author-one");
        AuthorizeAs(userId, "Author One", "author1@test.com", "platform-author");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/author-space");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AuthorSpaceDetailResponse? dto = await ReadDetailResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal(userId, dto.AuthorId);
        Assert.Equal("author-one", dto.Slug);
        Assert.NotNull(dto.FeatureFlags);
    }

    [Fact]
    public async Task GetMySpace_AsAuthor_WithoutSpace_ShouldReturn404()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Author No Space", "authorno@test.com", "platform-author");
        AuthorizeAs(userId, "Author No Space", "authorno@test.com", "platform-author");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/author-space");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMySpace_Twice_ShouldReturnSameSpace()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Author Two", "author2@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "author-two");
        AuthorizeAs(userId, "Author Two", "author2@test.com", "platform-author");

        HttpResponseMessage response1 = await HttpClient.GetAsync("/users/me/author-space");
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        AuthorSpaceDetailResponse? dto1 = await ReadDetailResponseAsync(response1);

        HttpResponseMessage response2 = await HttpClient.GetAsync("/users/me/author-space");
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        AuthorSpaceDetailResponse? dto2 = await ReadDetailResponseAsync(response2);

        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.Equal(dto1.AuthorId, dto2.AuthorId);
        Assert.Equal(dto1.Slug, dto2.Slug);
        Assert.Equal(dto1.CreatedAt, dto2.CreatedAt);

        // Verify only one record in DB
        int count = await ExecuteInDb(async db =>
            await db.AuthorSpaces.CountAsync(s => s.Id == userId));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task GetMySpace_WithoutAuthorRole_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Student Only", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/author-space");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMySpace_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me/author-space");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── GetBySlug ───────────────────────────────────────────────

    [Fact]
    public async Task GetBySlug_ExistingSpace_ShouldReturnPublicData()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Public Author", "public@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "public-author");

        // Fetch by slug (anonymous access)
        ClearAuthorization();
        HttpResponseMessage response = await HttpClient.GetAsync("/users/author-spaces/by-slug/public-author");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AuthorSpacePublicResponse? dto = await ReadPublicResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal(userId, dto.AuthorId);
        Assert.Equal("public-author", dto.Slug);
        Assert.Equal("Public Author", dto.DisplayName);
        Assert.NotNull(dto.FeatureFlags);
    }

    [Fact]
    public async Task GetBySlug_NonExistent_ShouldReturn404()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/author-spaces/by-slug/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GetAll ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ShouldReturnSpaces()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Listed Author", "listed@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "listed-author");

        // Fetch all (anonymous)
        ClearAuthorization();
        HttpResponseMessage response = await HttpClient.GetAsync("/users/author-spaces/?limit=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        JsonElement result = root.GetProperty("result");
        JsonElement itemsElement = result.GetProperty("items");
        List<AuthorSpaceListItem>? items = itemsElement.Deserialize<List<AuthorSpaceListItem>>(_jsonOptions);

        Assert.NotNull(items);
        Assert.Contains(items, x => x.AuthorId == userId);

        AuthorSpaceListItem item = items.First(x => x.AuthorId == userId);
        Assert.Equal("Listed Author", item.DisplayName);
        Assert.Equal("listed-author", item.Slug);
    }

    // ── UpdateSpace ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateSpace_ShouldUpdateTaglineAndFlags()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Update Author", "update@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "update-author");
        AuthorizeAs(userId, "Update Author", "update@test.com", "platform-author");

        // Update with tagline and flags
        var updateRequest = new UpdateAuthorSpaceRequest(
            "My awesome space",
            null,
            new AuthorSpaceFeatureFlagsDto(true, true, false, true));

        HttpResponseMessage updateResponse = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space",
            updateRequest);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Verify via GET
        HttpResponseMessage getResponse = await HttpClient.GetAsync("/users/me/author-space");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.DoesNotContain("leaderboard", await getResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("roadmaps", await getResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        AuthorSpaceDetailResponse? dto = await ReadDetailResponseAsync(getResponse);
        Assert.NotNull(dto);
        Assert.Equal("My awesome space", dto.Tagline);
        Assert.NotNull(dto.FeatureFlags);
        Assert.True(dto.FeatureFlags.GitHubIntegration);
        Assert.True(dto.FeatureFlags.PrReviews);
        Assert.False(dto.FeatureFlags.AiAssistant);
        Assert.True(dto.FeatureFlags.CustomLanding);
    }

    [Fact]
    public async Task UpdateSpace_WithoutAuthorRole_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        var updateRequest = new UpdateAuthorSpaceRequest("tagline", null, null);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space",
            updateRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSpace_WithoutCreatedSpace_ShouldReturn404()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "No Space", "nospace@test.com", "platform-author");
        AuthorizeAs(userId, "No Space", "nospace@test.com", "platform-author");

        // Do NOT call GetMySpace (no lazy-create), go straight to update
        var updateRequest = new UpdateAuthorSpaceRequest("tagline", null, null);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space",
            updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── UpdateSlug ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateSlug_ShouldChangeSlug()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Slug Author", "slug@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "slug-author");
        AuthorizeAs(userId, "Slug Author", "slug@test.com", "platform-author");

        // Update slug
        HttpResponseMessage updateResponse = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space/slug",
            new UpdateSlugRequest("new-custom-slug"));

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        // Verify via GET
        HttpResponseMessage getResponse = await HttpClient.GetAsync("/users/me/author-space");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.DoesNotContain("leaderboard", await getResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("roadmaps", await getResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        AuthorSpaceDetailResponse? dto = await ReadDetailResponseAsync(getResponse);
        Assert.NotNull(dto);
        Assert.Equal("new-custom-slug", dto.Slug);
    }

    [Fact]
    public async Task UpdateSlug_TakenSlug_ShouldReturnConflict()
    {
        // Create first author with space (slug = "taken-slug")
        Guid userId1 = Guid.NewGuid();
        await SeedUserAsync(userId1, "Author A", "authora@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId1, "taken-slug");

        // Create second author with space
        Guid userId2 = Guid.NewGuid();
        await SeedUserAsync(userId2, "Author B", "authorb@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId2, "author-b");
        AuthorizeAs(userId2, "Author B", "authorb@test.com", "platform-author");

        // Try to use the same slug as Author A
        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space/slug",
            new UpdateSlugRequest("taken-slug"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSlug_InvalidSlug_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Invalid Slug", "invalid@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "invalid-slug");
        AuthorizeAs(userId, "Invalid Slug", "invalid@test.com", "platform-author");

        // Try invalid slug (starts with dash)
        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space/slug",
            new UpdateSlugRequest("-invalid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSlug_WithoutAuthorRole_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space/slug",
            new UpdateSlugRequest("my-slug"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateSlug_SameSlug_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Same Slug", "same@test.com", "platform-author");
        await SeedAuthorSpaceAsync(userId, "my-slug");
        AuthorizeAs(userId, "Same Slug", "same@test.com", "platform-author");

        // Update with the same slug (should not fail)
        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author-space/slug",
            new UpdateSlugRequest("my-slug"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static async Task<AuthorSpaceDetailResponse?> ReadDetailResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out JsonElement wrappedResult))
        {
            return wrappedResult.Deserialize<AuthorSpaceDetailResponse>(_jsonOptions);
        }

        return root.Deserialize<AuthorSpaceDetailResponse>(_jsonOptions);
    }

    private static async Task<AuthorSpacePublicResponse?> ReadPublicResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out JsonElement wrappedResult))
        {
            return wrappedResult.Deserialize<AuthorSpacePublicResponse>(_jsonOptions);
        }

        return root.Deserialize<AuthorSpacePublicResponse>(_jsonOptions);
    }

    private static async Task<List<AuthorSpaceListItem>?> ReadListResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("result", out JsonElement wrappedResult))
        {
            return wrappedResult.Deserialize<List<AuthorSpaceListItem>>(_jsonOptions);
        }

        return root.Deserialize<List<AuthorSpaceListItem>>(_jsonOptions);
    }
}