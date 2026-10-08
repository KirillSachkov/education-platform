using System.Net;
using System.Net.Http.Headers;
using AuthService.Core.Features.Auth.GitHub;
using AuthService.Domain;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using PlatformAuth.Authorization;

namespace AuthService.IntegrationTests.Features.Auth;

[Collection(nameof(IntegrationTestFixture))]
public class GitHubOAuthCallbackTests : IntegrationTestsBase
{
    private const string CALLBACK_PATH = "/auth/github/oidc-callback";

    public GitHubOAuthCallbackTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public void GitHubClientRegistration_ShouldValidateGitHubAuthorizationResponseIssuer()
    {
        OpenIddictClientOptions options = Services
            .GetRequiredService<IOptionsMonitor<OpenIddictClientOptions>>()
            .CurrentValue;

        OpenIddictClientRegistration registration = Assert.Single(
            options.Registrations,
            candidate => string.Equals(
                candidate.ProviderName,
                GitHubRoutes.PROVIDER_NAME,
                StringComparison.Ordinal));

        Assert.Equal(new Uri(GitHubRoutes.AUTHORIZATION_SERVER_ISSUER), registration.Issuer);
        Assert.True(registration.Configuration?.AuthorizationResponseIssParameterSupported);
    }

    [Fact]
    public async Task GitHubCallback_WithGitHubIssuer_ShouldCompleteLinkAndPersistLogin()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(
            userId,
            "GitHub Link User",
            "github-link-user@test.com",
            [PlatformRoles.PARTICIPANT]);

        using WebApplicationFactory<Web.Program> callbackFactory = CreateCallbackFactory();
        using HttpClient callbackClient = callbackFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        (string state, string cookieHeader) = await StartGitHubLinkAsync(callbackClient, userId);
        using HttpRequestMessage callbackRequest = CreateCallbackRequest(
            state,
            cookieHeader,
            GitHubRoutes.AUTHORIZATION_SERVER_ISSUER);

        HttpResponseMessage callback = await callbackClient.SendAsync(callbackRequest);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains(
            "/settings/integrations?account=github-linked",
            callback.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        UserLoginInfo login = Assert.Single(
            await userManager.GetLoginsAsync(user),
            candidate => candidate.LoginProvider == GitHubRoutes.PROVIDER_NAME);

        Assert.Equal("12345678", login.ProviderKey);

        string orgSlug = await ExecuteInDb(db => db.UserGithubOrgs
            .Where(org => org.UserId == userId)
            .Select(org => org.OrgSlug)
            .SingleAsync());
        Assert.Equal("sachkovtech", orgSlug);
    }

    [Fact]
    public async Task GitHubCallback_WithUnexpectedIssuer_ShouldRejectWithoutPersistingLogin()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserAsync(
            userId,
            "Rejected GitHub User",
            "rejected-github-user@test.com",
            PlatformRoles.PARTICIPANT);

        using WebApplicationFactory<Web.Program> callbackFactory = CreateCallbackFactory();
        using HttpClient callbackClient = callbackFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        (string state, string cookieHeader) = await StartGitHubLinkAsync(callbackClient, userId);
        using HttpRequestMessage callbackRequest = CreateCallbackRequest(
            state,
            cookieHeader,
            "https://attacker.example/oauth");

        HttpResponseMessage callback = await callbackClient.SendAsync(callbackRequest);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains(
            "/login?error=github_auth_failed",
            callback.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        Assert.Empty(await userManager.GetLoginsAsync(user));
    }

    [Fact]
    public async Task GitHubCallback_WhenSameAccountAlreadyLinked_ShouldRestoreMissingProjection()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(
            userId,
            "Broken Projection User",
            "broken-proj@test.com",
            [PlatformRoles.PARTICIPANT]);
        await AddGitHubLoginAsync(userId, "12345678");

        HttpResponseMessage callback = await RunLinkCallbackAsync(userId);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains(
            "/settings/integrations?account=github-already-linked",
            callback.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        UserProfile profile = await ExecuteInDb(db => db.UserProfiles.SingleAsync(p => p.Id == userId));
        Assert.Equal("https://github.com/test-github-user", profile.Profiles!.Student!.GitHubUrl!.Value);

        string orgSlug = await ExecuteInDb(db => db.UserGithubOrgs
            .Where(org => org.UserId == userId)
            .Select(org => org.OrgSlug)
            .SingleAsync());
        Assert.Equal("sachkovtech", orgSlug);
    }

    [Fact]
    public async Task GitHubCallback_WhenDifferentAccountAlreadyLinked_ShouldNotTouchProjection()
    {
        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(
            userId,
            "Other GitHub User",
            "other-github-user@test.com",
            [PlatformRoles.PARTICIPANT]);
        await AddGitHubLoginAsync(userId, "87654321");

        HttpResponseMessage callback = await RunLinkCallbackAsync(userId);

        Assert.Contains(
            "/settings/integrations?account=github-already-linked",
            callback.Headers.Location!.ToString(),
            StringComparison.Ordinal);
        UserProfile profile = await ExecuteInDb(db => db.UserProfiles.SingleAsync(p => p.Id == userId));
        Assert.Null(profile.Profiles?.Student?.GitHubUrl);
        Assert.False(await ExecuteInDb(db => db.UserGithubOrgs.AnyAsync(org => org.UserId == userId)));
    }

    [Fact]
    public async Task GitHubCallback_WhenAccountOwnedByAnotherUser_ShouldReturnConflictWithoutMutation()
    {
        Guid ownerId = Guid.NewGuid();
        await SeedUserAsync(ownerId, "GitHub Owner", "github-owner@test.com", PlatformRoles.PARTICIPANT);
        await AddGitHubLoginAsync(ownerId, "12345678");

        Guid userId = Guid.NewGuid();
        await SeedUserWithProfileAsync(
            userId,
            "Conflicting User",
            "conflicting-user@test.com",
            [PlatformRoles.PARTICIPANT]);

        HttpResponseMessage callback = await RunLinkCallbackAsync(userId);

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Contains(
            "/settings/integrations?account=github-link-conflict",
            callback.Headers.Location!.ToString(),
            StringComparison.Ordinal);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        Assert.Empty(await userManager.GetLoginsAsync(user));
        UserProfile profile = await ExecuteInDb(db => db.UserProfiles.SingleAsync(p => p.Id == userId));
        Assert.Null(profile.Profiles?.Student?.GitHubUrl);
        Assert.False(await ExecuteInDb(db => db.UserGithubOrgs.AnyAsync(org => org.UserId == userId)));
    }

    private async Task<HttpResponseMessage> RunLinkCallbackAsync(Guid userId)
    {
        using WebApplicationFactory<Web.Program> callbackFactory = CreateCallbackFactory();
        using HttpClient callbackClient = callbackFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        (string state, string cookieHeader) = await StartGitHubLinkAsync(callbackClient, userId);
        using HttpRequestMessage callbackRequest = CreateCallbackRequest(
            state,
            cookieHeader,
            GitHubRoutes.AUTHORIZATION_SERVER_ISSUER);

        return await callbackClient.SendAsync(callbackRequest);
    }

    private async Task AddGitHubLoginAsync(Guid userId, string providerKey)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Account>>();
        Account user = (await userManager.FindByIdAsync(userId.ToString()))!;
        IdentityResult result = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(GitHubRoutes.PROVIDER_NAME, providerKey, "seeded"));
        Assert.True(result.Succeeded);
    }

    private WebApplicationFactory<Web.Program> CreateCallbackFactory() =>
        Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddGitHubOAuthPipelineTestDoubles()));

    private static async Task<(string State, string CookieHeader)> StartGitHubLinkAsync(
        HttpClient callbackClient,
        Guid userId)
    {
        callbackClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                TestAuthHandler.SchemeName,
                $"{userId}|GitHub Link User|github-link-user@test.com|{PlatformRoles.PARTICIPANT}");

        HttpResponseMessage challenge = await callbackClient.GetAsync("/auth/github/link");

        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Uri authorizationUri = challenge.Headers.Location!;
        string state = Assert.Single(QueryHelpers.ParseQuery(authorizationUri.Query)["state"]!)!;
        string cookieHeader = string.Join(
            "; ",
            challenge.Headers.GetValues("Set-Cookie")
                .Select(value => value[..value.IndexOf(';', StringComparison.Ordinal)]));

        return (state, cookieHeader);
    }

    private static HttpRequestMessage CreateCallbackRequest(
        string state,
        string cookieHeader,
        string issuer)
    {
        string callbackUri = QueryHelpers.AddQueryString(
            CALLBACK_PATH,
            new Dictionary<string, string?>
            {
                ["code"] = "test-authorization-code",
                ["state"] = state,
                ["iss"] = issuer,
            });

        var request = new HttpRequestMessage(HttpMethod.Get, callbackUri);
        request.Headers.Add("Cookie", cookieHeader);
        return request;
    }
}
