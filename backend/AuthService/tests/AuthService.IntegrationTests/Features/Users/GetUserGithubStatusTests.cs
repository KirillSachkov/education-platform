using System.Net;
using System.Net.Http.Json;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace AuthService.IntegrationTests.Features.Users;

/// <summary>
///     Tests for <c>GET /users/{userId}/github-status</c> (#444) — admin/support GitHub snapshot
///     feeding the «Доступы и сообщества» panel. Verifies auth gates, the not-linked default and
///     the linked-with-orgs path (Identity external login + <c>user_github_orgs</c> cache).
/// </summary>
[Collection(nameof(IntegrationTestFixture))]
public class GetUserGithubStatusTests : IntegrationTestsBase
{
    public GetUserGithubStatusTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetGithubStatus_Anonymous_ShouldReturnUnauthorized()
    {
        ClearAuthorization();

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{Guid.NewGuid()}/github-status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetGithubStatus_WithoutViewPermission_ShouldReturnForbidden()
    {
        AuthorizeAs(Guid.NewGuid(), "Student", "student@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{Guid.NewGuid()}/github-status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetGithubStatus_UserWithoutGithub_ShouldReturnNotLinked()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid target = Guid.NewGuid();
        await SeedUserAsync(target, "Buyer", "buyer@test.com", "platform-participant");

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{target}/github-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EnvelopeStub<GithubStatusBody>? body =
            await response.Content.ReadFromJsonAsync<EnvelopeStub<GithubStatusBody>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);
        Assert.False(body.Result!.Linked);
        Assert.Null(body.Result.GithubUsername);
        Assert.Null(body.Result.GithubUserId);
        Assert.Empty(body.Result.Orgs);
    }

    [Fact]
    public async Task GetGithubStatus_LinkedWithOrgs_ShouldReturnIdentityAndOrgs()
    {
        Guid adminId = Guid.NewGuid();
        await SeedUserAsync(adminId, "Admin", "admin@test.com", "platform-admin");
        AuthorizeAs(adminId, "Admin", "admin@test.com", "platform-admin");

        Guid target = Guid.NewGuid();
        await SeedUserAsync(target, "Buyer", "buyer@test.com", "platform-participant");

        await ExecuteInDb(async db =>
        {
            db.Set<IdentityUserLogin<Guid>>().Add(new IdentityUserLogin<Guid>
            {
                LoginProvider = "GitHub",
                ProviderKey = "424242",
                ProviderDisplayName = "octobuyer",
                UserId = target,
            });
            db.Set<UserGithubOrg>().Add(new UserGithubOrg
            {
                UserId = target,
                OrgSlug = "acme-co",
                SyncedAt = DateTime.UtcNow,
            });
            db.Set<UserGithubOrg>().Add(new UserGithubOrg
            {
                UserId = target,
                OrgSlug = "beta-org",
                SyncedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await HttpClient.GetAsync($"/users/{target}/github-status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EnvelopeStub<GithubStatusBody>? body =
            await response.Content.ReadFromJsonAsync<EnvelopeStub<GithubStatusBody>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);
        Assert.True(body.Result!.Linked);
        Assert.Equal("octobuyer", body.Result.GithubUsername);
        Assert.Equal("424242", body.Result.GithubUserId);
        Assert.Equal(2, body.Result.Orgs.Count);
        Assert.Contains(body.Result.Orgs, o => o.Slug == "acme-co");
        Assert.Contains(body.Result.Orgs, o => o.Slug == "beta-org");
    }

    private sealed class EnvelopeStub<T>
    {
        public T? Result { get; set; }

        public bool IsError { get; set; }
    }

    private sealed class GithubStatusBody
    {
        public bool Linked { get; set; }

        public string? GithubUserId { get; set; }

        public string? GithubUsername { get; set; }

        public List<OrgBody> Orgs { get; set; } = [];
    }

    private sealed class OrgBody
    {
        public string Slug { get; set; } = string.Empty;

        public DateTime SyncedAt { get; set; }
    }
}
