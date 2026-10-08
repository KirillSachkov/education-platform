using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.GitHubApp;

namespace AssignmentReviewService.IntegrationTests.Features.Installations;

public sealed class CompleteInstallationTests : AssignmentReviewServiceTestsBase
{
    private const long INSTALLATION_ID = 12345L;

    public CompleteInstallationTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Callback_HappyPath_CreatesVcsInstallationAndRedirects()
    {
        // Arrange — pre-populate state-token via injected store.
        Guid userId = Guid.NewGuid();
        IInstallStateStore<InstallStateData> store = Services.GetRequiredService<IInstallStateStore<InstallStateData>>();
        const string stateToken = "test-state-token-happy-path";
        await store.SetAsync(stateToken, new InstallStateData(userId, "/custom/return"), TimeSpan.FromMinutes(10));

        // FakeVcsProvider default возвращает {test-user, 9999, USER, AllRepos}.

        HttpClient noRedirectClient = Factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
        });

        // Act
        HttpResponseMessage response = await noRedirectClient.GetAsync(
            $"/assignment-review/installations/callback/?installation_id={INSTALLATION_ID}&state={stateToken}");

        // Assert
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/custom/return", response.Headers.Location?.OriginalString);

        string installationIdStr = INSTALLATION_ID.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation? installation = await db.VcsInstallations
                .FirstOrDefaultAsync(i => i.InstallationId == installationIdStr);
            Assert.NotNull(installation);
            Assert.Equal(userId, installation!.LinkedUserId);
            Assert.Equal("test-user", installation.OwnerLogin);
            Assert.Equal(VcsInstallationStatus.ACTIVE, installation.Status);
            Assert.True(installation.RepoSelections.All);
        });
    }

    [Fact]
    public async Task Callback_BadStateToken_RedirectsWithError()
    {
        HttpClient noRedirectClient = Factory.CreateClient(new() { AllowAutoRedirect = false });

        HttpResponseMessage response = await noRedirectClient.GetAsync(
            $"/assignment-review/installations/callback/?installation_id={INSTALLATION_ID}&state=does-not-exist");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/settings/integrations?installation=error", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Contains("code=vcs.install_state.invalid", response.Headers.Location.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_MissingStateToken_RedirectsWithError()
    {
        HttpClient noRedirectClient = Factory.CreateClient(new() { AllowAutoRedirect = false });

        HttpResponseMessage response = await noRedirectClient.GetAsync(
            $"/assignment-review/installations/callback/?installation_id={INSTALLATION_ID}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/settings/integrations?installation=error", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Callback_StateConsumedTwice_SecondCallRedirectsWithError()
    {
        Guid userId = Guid.NewGuid();
        IInstallStateStore<InstallStateData> store = Services.GetRequiredService<IInstallStateStore<InstallStateData>>();
        const string stateToken = "test-state-token-consumed-twice";
        await store.SetAsync(stateToken, new InstallStateData(userId, null), TimeSpan.FromMinutes(10));

        HttpClient noRedirectClient = Factory.CreateClient(new() { AllowAutoRedirect = false });

        HttpResponseMessage first = await noRedirectClient.GetAsync(
            $"/assignment-review/installations/callback/?installation_id={INSTALLATION_ID}&state={stateToken}");
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        HttpResponseMessage second = await noRedirectClient.GetAsync(
            $"/assignment-review/installations/callback/?installation_id={INSTALLATION_ID}&state={stateToken}");
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Contains("installation=error", second.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }
}
