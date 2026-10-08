using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Integrations.GitHub;
using AccessService.Domain.Integrations.GitHub;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Integrations.GitHubApp;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetInstallationStatusTests : AccessServiceTestsBase
{
    public GetInstallationStatusTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Returns_not_installed_when_no_record_for_caller()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/integrations/github/installation-status/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GithubInstallationStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GithubInstallationStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.False(envelope.Result.IsInstalled);
        Assert.Null(envelope.Result.OrgLogin);
        Assert.Null(envelope.Result.InstalledAt);
        Assert.False(envelope.Result.IsSuspended);
    }

    [Fact]
    public async Task Returns_installed_with_org_when_record_exists_for_caller()
    {
        DateTimeOffset installedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(
                AuthorGithubInstallation.Create(CurrentUserId, 12_345, "SachkovTech", installedAt));
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/integrations/github/installation-status/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GithubInstallationStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GithubInstallationStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.True(envelope.Result.IsInstalled);
        Assert.Equal("sachkovtech", envelope.Result.OrgLogin);
        Assert.False(envelope.Result.IsSuspended);
        Assert.Equal(installedAt, envelope.Result.InstalledAt);
    }

    [Fact]
    public async Task Returns_suspended_when_installation_is_suspended()
    {
        DateTimeOffset installedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await ExecuteInDbAsync(async db =>
        {
            AuthorGithubInstallation installation = AuthorGithubInstallation.Create(
                CurrentUserId, 12_345, "sachkovtech", installedAt);
            installation.Suspend(installedAt.AddDays(1));
            db.AuthorGithubInstallations.Add(installation);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/integrations/github/installation-status/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GithubInstallationStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GithubInstallationStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.True(envelope.Result.IsInstalled);
        Assert.True(envelope.Result.IsSuspended);
    }

    [Fact]
    public async Task Scoped_to_caller_only()
    {
        Guid otherAuthorId = Guid.NewGuid();
        DateTimeOffset installedAt = new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(
                AuthorGithubInstallation.Create(otherAuthorId, 999, "other-org", installedAt));
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/integrations/github/installation-status/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<GithubInstallationStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GithubInstallationStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.False(envelope.Result.IsInstalled);
        Assert.Null(envelope.Result.OrgLogin);
    }

    [Fact]
    public async Task Requires_plans_manage_permission()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/integrations/github/installation-status/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
