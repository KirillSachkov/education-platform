using System.Net;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using AuthService.Domain;
using AuthService.Core.Database;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class SessionAndGitHubTests : IntegrationTestsBase
{
    public SessionAndGitHubTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    // ── RevokeAllSessions ───────────────────────────────────────

    [Fact]
    public async Task RevokeAllSessions_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/sessions/revoke-all", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_AuthenticatedUser_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Revoker", "revoker@test.com", "platform-participant");
        AuthorizeAs(userId, "Revoker", "revoker@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/sessions/revoke-all", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_ShouldUpdateSecurityStamp()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "StampCheck", "stampcheck@test.com", "platform-participant");
        AuthorizeAs(userId, "StampCheck", "stampcheck@test.com", "platform-participant");

        // Get stamp before
        string stampBefore = await GetSecurityStampAsync(userId);

        await HttpClient.PostAsync("/users/me/sessions/revoke-all", null);

        // Get stamp after
        string stampAfter = await GetSecurityStampAsync(userId);

        Assert.NotEqual(stampBefore, stampAfter);
    }

    [Fact]
    public async Task RevokeAllSessions_WithoutPermission_ShouldReturnForbidden()
    {
        Guid userId = Guid.NewGuid();
        AuthorizeAs(userId, "NoPerms", "noperms@test.com", "unknown-role");

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/sessions/revoke-all", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── UnlinkGitHub ────────────────────────────────────────────

    [Fact]
    public async Task UnlinkGitHub_AnonymousRequest_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/github/unlink", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkGitHub_UserWithGitHubLinked_ShouldSucceed()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "GitHubber", "githubber@test.com", "platform-participant");
        await AddGitHubLoginAsync(userId);
        AuthorizeAs(userId, "GitHubber", "githubber@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/github/unlink", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnlinkGitHub_ShouldClearCachedOrganizationsAndPublishEmptySnapshot()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "GitHubber", "github-orgs@test.com", "platform-participant");
        await AddGitHubLoginAsync(userId);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IUserGithubOrgRepository repository =
                scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();
            await repository.ReplaceAllAsync(
                userId, ["example-org"], DateTime.UtcNow, CancellationToken.None);
        }

        AuthorizeAs(userId, "GitHubber", "github-orgs@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/github/unlink", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using AsyncServiceScope verificationScope = Services.CreateAsyncScope();
        IUserGithubOrgRepository verificationRepository =
            verificationScope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();
        Assert.Empty(await verificationRepository.GetByUserAsync(userId, CancellationToken.None));

        UserGithubLogin message = Assert.Single(OutboxCollector.OfType<UserGithubLogin>());
        Assert.Equal(userId, message.UserId);
        Assert.Empty(message.GithubOrgs);
    }

    [Fact]
    public async Task UnlinkGitHub_UserWithoutGitHubLinked_ShouldReturnBadRequest()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "NoGitHub", "nogithub@test.com", "platform-participant");
        AuthorizeAs(userId, "NoGitHub", "nogithub@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.PostAsync(
            "/users/me/github/unlink", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GetMyProfile HasPassword / HasGitHubLinked ────────────────

    [Fact]
    public async Task GetMyProfile_UserWithPassword_ShouldReturnHasPasswordTrue()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithPasswordAsync(userId, "Pass123!", "HasPw", "haspw@test.com", "platform-participant");
        await SeedProfileForUserAsync(userId);
        AuthorizeAs(userId, "HasPw", "haspw@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"hasPassword\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMyProfile_UserWithoutPassword_ShouldReturnHasPasswordFalse()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "NoPw", "nopw@test.com", "platform-participant");
        await SeedProfileForUserAsync(userId);
        AuthorizeAs(userId, "NoPw", "nopw@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"hasPassword\":false", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMyProfile_UserWithGitHub_ShouldReturnHasGitHubLinkedTrue()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "GH", "gh@test.com", "platform-participant");
        await SeedProfileForUserAsync(userId);
        await AddGitHubLoginAsync(userId);
        AuthorizeAs(userId, "GH", "gh@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"hasGitHubLinked\":true", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMyProfile_UserWithoutGitHub_ShouldReturnHasGitHubLinkedFalse()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "NoGH", "nogh@test.com", "platform-participant");
        await SeedProfileForUserAsync(userId);
        AuthorizeAs(userId, "NoGH", "nogh@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync("/users/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"hasGitHubLinked\":false", body, StringComparison.Ordinal);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private async Task AddGitHubLoginAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        await userManager.AddLoginAsync(user!, new UserLoginInfo("GitHub", $"gh-{userId}", "GitHub"));
    }

    private async Task<string> GetSecurityStampAsync(Guid userId)
    {
        await using var scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GetSecurityStampAsync(user!);
    }

    private async Task SeedProfileForUserAsync(Guid userId)
    {
        await ExecuteInDb(async db =>
        {
            db.UserProfiles.Add(new UserProfile
            {
                Id = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });
    }
}
