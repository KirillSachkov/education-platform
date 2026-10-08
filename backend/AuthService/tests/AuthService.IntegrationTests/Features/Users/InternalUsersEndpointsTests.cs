using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts;
using AuthService.Core.Database;
using AuthService.Domain;
using AuthService.Domain.ValueObjects;
using AuthService.IntegrationTests.Infrastructure;
using Core.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AuthService.IntegrationTests.Features.Users;

[Collection(nameof(IntegrationTestFixture))]
public class InternalUsersEndpointsTests : IntegrationTestsBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public InternalUsersEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetByEmail_AsServiceRole_ReturnsUser()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "Student", "student-internal@test.com", "platform-student");
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-email?email=student-internal@test.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetByEmail_WithoutServiceOrAdminRole_ReturnsForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-student");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-email?email=student@test.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetByEmail_UnknownUser_ReturnsNotFound()
    {
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-email?email=missing@test.com");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Batch_AsServiceRole_ReturnsMatchedUsers()
    {
        Guid userId1 = Guid.NewGuid();
        Guid userId2 = Guid.NewGuid();
        await SeedUserAsync(userId1, "User1", "batch-user1@test.com", "platform-student");
        await SeedUserAsync(userId2, "User2", "batch-user2@test.com", "platform-student");
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/users/batch",
            new { userIds = new[] { userId1, userId2 } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Batch_AnonymousRequest_ReturnsUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.PostAsJsonAsync(
            "/internal/users/batch",
            new { userIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUserIdsByGithubOrg_AsServiceRole_ReturnsMembers()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        await SeedUserAsync(userA, "A", "ghorg-a@test.com", "platform-student");
        await SeedUserAsync(userB, "B", "ghorg-b@test.com", "platform-student");
        await SeedUserAsync(otherUser, "Other", "ghorg-other@test.com", "platform-student");

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IUserGithubOrgRepository>();
            await repo.ReplaceAllAsync(userA, ["match-org"], DateTime.UtcNow, default);
            await repo.ReplaceAllAsync(userB, ["match-org", "extra"], DateTime.UtcNow, default);
            await repo.ReplaceAllAsync(otherUser, ["different"], DateTime.UtcNow, default);
        }

        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-org/match-org");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<UserIdsByGithubOrgResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UserIdsByGithubOrgResponse>>(_jsonOptions);
        Assert.NotNull(envelope?.Result);
        UserIdsByGithubOrgResponse body = envelope.Result!;
        Assert.Equal("match-org", body.OrgSlug);
        Assert.Equal(2, body.UserIds.Count);
        Assert.Contains(userA, body.UserIds);
        Assert.Contains(userB, body.UserIds);
        Assert.DoesNotContain(otherUser, body.UserIds);
    }

    [Fact]
    public async Task GetUserIdsByGithubOrg_NoMembers_ReturnsEmptyList()
    {
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-org/nobody-here");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<UserIdsByGithubOrgResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UserIdsByGithubOrgResponse>>(_jsonOptions);
        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope.Result!.UserIds);
    }

    [Fact]
    public async Task GetUserIdsByGithubOrg_AsStudent_ReturnsForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-student");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-org/any-org");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── #451: by-github-id (резолв юзера установки GitHub App для ARS recovery) ────

    [Fact]
    public async Task GetUserIdByGithubId_AsServiceRole_ReturnsLinkedUser()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "GH", "ghid-linked@test.com", "platform-student");
        await AddGitHubLoginAsync(userId, "778899");
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-id/778899");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<UserIdByGithubIdResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UserIdByGithubIdResponse>>(_jsonOptions);
        Assert.NotNull(envelope?.Result);
        Assert.Equal("778899", envelope.Result!.ExternalId);
        Assert.Equal(userId, envelope.Result.UserId);
    }

    [Fact]
    public async Task GetUserIdByGithubId_UnknownGithubId_ReturnsNullUser()
    {
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-id/000-nobody");

        // 200 с UserId=null — «нет привязки», не ошибка (caller трактует как skip recovery).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<UserIdByGithubIdResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UserIdByGithubIdResponse>>(_jsonOptions);
        Assert.NotNull(envelope?.Result);
        Assert.Null(envelope.Result!.UserId);
    }

    [Fact]
    public async Task GetUserIdByGithubId_AsStudent_ReturnsForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-student");

        HttpResponseMessage response = await HttpClient.GetAsync(
            "/internal/users/by-github-id/778899");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUserGithubLogin_ReturnsCanonicalProfileLogin_NotProviderDisplayName()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(userId, "GH", "github-login@test.com", "platform-student");
        await AddGitHubLoginAsync(userId, "778899", providerDisplayName: "Unrelated Display Name");
        await SetGithubProfileAsync(userId, "canonical-login");
        AuthorizeAs(Guid.NewGuid(), "Service", "service@test.com", "platform-service");

        HttpResponseMessage response = await HttpClient.GetAsync(
            $"/internal/users/{userId}/github-login/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<UserGithubLoginResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<UserGithubLoginResponse>>(_jsonOptions);
        Assert.Equal("canonical-login", envelope!.Result!.GithubLogin);
    }

    private async Task AddGitHubLoginAsync(
        Guid userId,
        string externalId,
        string providerDisplayName = "GitHub")
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        UserManager<Account> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account? user = await userManager.FindByIdAsync(userId.ToString());
        await userManager.AddLoginAsync(user!, new UserLoginInfo("GitHub", externalId, providerDisplayName));
    }

    private async Task SetGithubProfileAsync(Guid userId, string login)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IProfileRepository profiles = scope.ServiceProvider.GetRequiredService<IProfileRepository>();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        await profiles.EnsureExistsAsync(userId, default);
        UserProfile profile = (await profiles.GetByAsync(p => p.Id == userId, default))!;
        profile.SetGitHubUrl(GitHubUrl.Create($"https://github.com/{login}").Value, DateTime.UtcNow);
        Assert.True((await transactions.SaveChangesAsync(default)).IsSuccess);
    }
}
