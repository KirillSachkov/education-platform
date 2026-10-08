using System.Net;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class GetMyProfileTests : IntegrationTestsBase
{
    public GetMyProfileTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyProfile_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProfile_AuthenticatedWithoutContentViewPermission_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "No Permissions", "noperms@test.com", "unknown-role");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyProfile_WithExistingProfile_ShouldReturnProfileData()
    {
        Guid userId = Guid.NewGuid();

        await SeedUserWithProfileAsync(userId, "Bob Smith", "bob@test.com",
            ["platform-participant", "platform-author"],
            new UserProfile
            {
                Id = userId,
                Bio = Bio.Create("Profile from database").Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });

        AuthorizeAs(userId, "Bob Smith", "bob@test.com", "platform-participant", "platform-author");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetMyProfileResponse? dto = await ReadProfileResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal(userId, dto.Id);
        Assert.Equal("Bob Smith", dto.Name);
        Assert.Equal("bob@test.com", dto.Email);
        Assert.Equal(["platform-participant", "platform-author"], dto.Roles);
        Assert.Equal("Profile from database", dto.Bio);
    }

    [Fact]
    public async Task GetMyProfile_WithNoProfile_ShouldReturnNullBioAndProfiles()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Alice", "alice@test.com", "platform-participant");
        AuthorizeAs(userId, "Alice", "alice@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetMyProfileResponse? dto = await ReadProfileResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal(userId, dto.Id);
        Assert.Equal("Alice", dto.Name);
        Assert.Null(dto.Bio);
        Assert.Null(dto.Profiles);
    }

    [Fact]
    public async Task GetMyProfile_ShouldReturnBioAndRoleProfiles_FromDatabase()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;

        await SeedUserWithProfileAsync(userId, "Carol", "carol@test.com",
            ["platform-participant"],
            new UserProfile
            {
                Id = userId,
                Bio = Bio.Create("Backend engineer and mentor").Value,
                CreatedAt = now,
                UpdatedAt = now,
                Profiles = new Profiles
                {
                    Student = new StudentProfile
                    {
                        GitHubUrl = GitHubUrl.Create("https://github.com/student-profile").Value,
                        UpdatedAt = now,
                    },
                    Author = new AuthorProfile
                    {
                        Specialization = Specialization.Create("Backend .NET").Value,
                        AboutAsAuthor = AboutAsAuthor.Create("Build practical backend courses").Value,
                        UpdatedAt = now,
                    },
                    Reviewer = new ReviewerProfile
                    {
                        ReviewCapacity = 7, Expertise = Expertise.Create("System Design").Value, UpdatedAt = now,
                    }
                }
            });

        AuthorizeAs(userId, "Carol", "carol@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetMyProfileResponse? dto = await ReadProfileResponseAsync(response);
        Assert.NotNull(dto);
        Assert.Equal("Backend engineer and mentor", dto.Bio);
        Assert.NotNull(dto.Profiles);

        Assert.NotNull(dto.Profiles.Student);
        Assert.Equal("https://github.com/student-profile", dto.Profiles.Student.GitHubUrl);

        Assert.NotNull(dto.Profiles.Author);
        Assert.Equal("Backend .NET", dto.Profiles.Author.Specialization);
        Assert.Equal("Build practical backend courses", dto.Profiles.Author.AboutAsAuthor);

        Assert.NotNull(dto.Profiles.Reviewer);
        Assert.Equal(7, dto.Profiles.Reviewer.ReviewCapacity);
        Assert.Equal("System Design", dto.Profiles.Reviewer.Expertise);
    }

    private static async Task<GetMyProfileResponse?> ReadProfileResponseAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

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
