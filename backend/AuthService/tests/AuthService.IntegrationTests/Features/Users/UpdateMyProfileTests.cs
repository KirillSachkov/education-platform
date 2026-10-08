using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using AuthService.Contracts;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using AuthService.IntegrationTests.Infrastructure;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class UpdateMyProfileTests : IntegrationTestsBase
{
    public UpdateMyProfileTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task UpdateMyBaseProfile_AnonymousRequest_ShouldReturnUnauthorized_AndNotCreateProfile()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/base",
            new UpdateMyBaseProfileRequest("Some bio"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        int count = await ExecuteInDb(async db =>
            await db.UserProfiles.CountAsync());

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateMyBaseProfile_AuthenticatedWithoutPermission_ShouldReturnForbidden_AndNotCreateProfile()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "No Permission", "noperms@test.com", "unknown-role");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/base",
            new UpdateMyBaseProfileRequest("Some bio"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int count = await ExecuteInDb(async db =>
            await db.UserProfiles.CountAsync(p => p.Id == userId));

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("/users/me/author")]
    [InlineData("/users/me/reviewer")]
    public async Task UpdateRoleProfile_AnonymousRequest_ShouldReturnUnauthorized_AndNotCreateProfile(string endpoint)
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            endpoint,
            CreateUpdateRequest(endpoint));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        int count = await ExecuteInDb(async db =>
            await db.UserProfiles.CountAsync());

        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("/users/me/author")]
    [InlineData("/users/me/reviewer")]
    public async Task UpdateRoleProfile_AuthenticatedWithoutPermission_ShouldReturnForbidden_AndNotCreateProfile(
        string endpoint)
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "No Permission", "noperms@test.com", "unknown-role");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            endpoint,
            CreateUpdateRequest(endpoint));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int count = await ExecuteInDb(async db =>
            await db.UserProfiles.CountAsync(p => p.Id == userId));

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task UpdateMyBaseProfile_ShouldUpdateBioAndUpdatedAt()
    {
        Guid userId = Guid.NewGuid();
        DateTime originalUpdatedAt = DateTime.UtcNow.AddDays(-1);

        await SeedUserWithProfileAsync(userId, "Student", "student@test.com",
            ["platform-participant"],
            new UserProfile
            {
                Id = userId,
                Bio = Bio.Create("Old bio").Value,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = originalUpdatedAt,
            });

        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/base",
            new UpdateMyBaseProfileRequest("New bio"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.Equal("New bio", profile!.Bio!.Value);
        Assert.True(profile.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateMyBaseProfile_WithTooLongBio_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId, "Student", "student@test.com", ["platform-participant"]);
        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        string tooLongBio = new('a', Bio.MAX_LENGTH + 1);

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/base",
            new UpdateMyBaseProfileRequest(tooLongBio));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyBaseProfile_WithWhitespaceBio_ShouldClearBio()
    {
        Guid userId = Guid.NewGuid();
        DateTime originalUpdatedAt = DateTime.UtcNow.AddDays(-1);

        await SeedUserWithProfileAsync(userId, "Student", "student@test.com",
            ["platform-participant"],
            new UserProfile
            {
                Id = userId,
                Bio = Bio.Create("Old bio").Value,
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = originalUpdatedAt,
            });

        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/base",
            new UpdateMyBaseProfileRequest("   "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.Null(profile.Bio);
        Assert.True(profile.UpdatedAt > originalUpdatedAt);
    }

    [Fact]
    public async Task UpdateMyAuthorProfile_WithoutAuthorRole_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author",
            new UpdateMyAuthorProfileRequest("Backend", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyReviewerProfile_WithoutReviewerRole_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/reviewer",
            new UpdateMyReviewerProfileRequest(3, "System Design"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyAuthorProfile_WithRole_ShouldCreateOrUpdateAuthorProfile()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId, "Author", "author@test.com", ["platform-author"]);
        AuthorizeAs(userId, "Author", "author@test.com", "platform-author");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author",
            new UpdateMyAuthorProfileRequest(
                "Backend .NET",
                "Teaching practical backend topics"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.NotNull(profile!.Profiles?.Author);
        Assert.Equal("Backend .NET", profile.Profiles!.Author!.Specialization!.Value);
        Assert.Equal("Teaching practical backend topics", profile.Profiles.Author.AboutAsAuthor!.Value);
    }

    [Fact]
    public async Task UpdateMyAuthorProfile_WithWhitespaceOptionalFields_ShouldClearAuthorFields()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow.AddDays(-1);

        await SeedUserWithProfileAsync(userId, "Author", "author@test.com",
            ["platform-author"],
            new UserProfile
            {
                Id = userId,
                CreatedAt = now.AddDays(-1),
                UpdatedAt = now,
                Profiles = new Profiles
                {
                    Author = new AuthorProfile
                    {
                        Specialization = Specialization.Create("Backend").Value,
                        AboutAsAuthor = AboutAsAuthor.Create("Old about").Value,
                        UpdatedAt = now
                    }
                }
            });

        AuthorizeAs(userId, "Author", "author@test.com", "platform-author");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author",
            new UpdateMyAuthorProfileRequest("   ", "   "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.NotNull(profile.Profiles?.Author);
        Assert.Null(profile.Profiles.Author.Specialization);
        Assert.Null(profile.Profiles.Author.AboutAsAuthor);
    }

    [Fact]
    public async Task UpdateMyAuthorProfile_ShouldPreserveOtherRoleProfiles()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow.AddDays(-1);

        await SeedUserWithProfileAsync(userId, "Author", "author@test.com",
            ["platform-author"],
            new UserProfile
            {
                Id = userId,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now,
                Profiles = new Profiles
                {
                    Student = new StudentProfile
                    {
                        GitHubUrl = GitHubUrl.Create("https://github.com/existing-student").Value,
                        UpdatedAt = now
                    },
                    Reviewer = new ReviewerProfile
                    {
                        ReviewCapacity = 4, Expertise = Expertise.Create("Architecture").Value, UpdatedAt = now
                    }
                }
            });

        AuthorizeAs(userId, "Author", "author@test.com", "platform-author");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/author",
            new UpdateMyAuthorProfileRequest(
                "Backend .NET",
                "Teaching practical backend topics"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.NotNull(profile!.Profiles);

        Assert.NotNull(profile.Profiles!.Author);
        Assert.Equal("Backend .NET", profile.Profiles.Author!.Specialization!.Value);

        Assert.NotNull(profile.Profiles.Student);
        Assert.Equal("https://github.com/existing-student", profile.Profiles.Student!.GitHubUrl!.Value);

        Assert.NotNull(profile.Profiles.Reviewer);
        Assert.Equal(4, profile.Profiles.Reviewer!.ReviewCapacity);
        Assert.Equal("Architecture", profile.Profiles.Reviewer.Expertise!.Value);
    }

    [Fact]
    public async Task UpdateMyReviewerProfile_WithRole_ShouldCreateOrUpdateReviewerProfile()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(userId, "Moderator", "moderator@test.com", ["platform-moderator"]);
        AuthorizeAs(userId, "Moderator", "moderator@test.com", "platform-moderator");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/reviewer",
            new UpdateMyReviewerProfileRequest(5, "System Design"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.NotNull(profile!.Profiles?.Reviewer);
        Assert.Equal(5, profile.Profiles!.Reviewer!.ReviewCapacity);
        Assert.Equal("System Design", profile.Profiles.Reviewer.Expertise!.Value);
    }

    [Fact]
    public async Task UpdateMyReviewerProfile_WithNegativeReviewCapacity_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "Moderator", "moderator@test.com", "platform-moderator");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/reviewer",
            new UpdateMyReviewerProfileRequest(-1, "System Design"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateMyReviewerProfile_WithWhitespaceExpertise_ShouldClearExpertise()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow.AddDays(-1);

        await SeedUserWithProfileAsync(userId, "Moderator", "moderator@test.com",
            ["platform-moderator"],
            new UserProfile
            {
                Id = userId,
                CreatedAt = now.AddDays(-2),
                UpdatedAt = now,
                Profiles = new Profiles
                {
                    Reviewer = new ReviewerProfile
                    {
                        ReviewCapacity = 5, Expertise = Expertise.Create("Algorithms").Value, UpdatedAt = now
                    }
                }
            });

        AuthorizeAs(userId, "Moderator", "moderator@test.com", "platform-moderator");

        HttpResponseMessage response = await HttpClient.PatchAsJsonAsync(
            "/users/me/reviewer",
            new UpdateMyReviewerProfileRequest(6, "   "));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        UserProfile? profile = await ExecuteInDb(async db =>
            await db.UserProfiles.FirstOrDefaultAsync(p => p.Id == userId));

        Assert.NotNull(profile);
        Assert.NotNull(profile.Profiles?.Reviewer);
        Assert.Equal(6, profile.Profiles.Reviewer.ReviewCapacity);
        Assert.Null(profile.Profiles.Reviewer.Expertise);
    }

    private static object CreateUpdateRequest(string endpoint) =>
        endpoint switch
        {
            "/users/me/author" => new UpdateMyAuthorProfileRequest("Backend", "Author bio"),
            "/users/me/reviewer" => new UpdateMyReviewerProfileRequest(3, "System Design"),
            _ => throw new InvalidOperationException($"Unknown endpoint: {endpoint}")
        };
}
