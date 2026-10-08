using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.Installations;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using PlatformAuth.Authorization;

namespace AssignmentReviewService.IntegrationTests.Features.Installations;

/// <summary>
///     Smoke-tests for <c>GET /assignment-review/installations/me</c>.
///     Используется фронтом в карточке /settings/integrations — возвращает
///     только installations linked to current user.
/// </summary>
public sealed class GetMyInstallationsTests : AssignmentReviewServiceTestsBase
{
    public GetMyInstallationsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GetMine_NoInstallations_ReturnsEmptyList()
    {
        // Arrange
        AuthenticateAs(PlatformRoles.AUTHOR);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/assignment-review/installations/me/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvelopeStub<GetMyInstallationsResponse>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);
        Assert.Empty(body.Result!.Installations);
    }

    [Fact]
    public async Task GetMine_ReturnsOnlyOwnInstallations()
    {
        // Arrange — два installation'а: один на нашего юзера, второй на другого.
        Guid me = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        VcsInstallation mineActive = VcsInstallation.Create(
            VcsProvider.GITHUB,
            installationId: "1001",
            VcsInstallationOwnerType.USER,
            ownerLogin: "alice",
            ownerExternalId: "100",
            linkedUserId: me,
            RepoSelections.AllRepos());

        VcsInstallation alienActive = VcsInstallation.Create(
            VcsProvider.GITHUB,
            installationId: "2002",
            VcsInstallationOwnerType.ORG,
            ownerLogin: "acme-co",
            ownerExternalId: "200",
            linkedUserId: other,
            RepoSelections.Specific(["acme-co/repo1"]));

        await ExecuteInDbAsync(async db =>
        {
            db.VcsInstallations.Add(mineActive);
            db.VcsInstallations.Add(alienActive);
            await db.SaveChangesAsync();
        });

        AuthenticateAs(PlatformRoles.AUTHOR, me);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/assignment-review/installations/me/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvelopeStub<GetMyInstallationsResponse>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);

        VcsInstallationDto[] installations = body.Result!.Installations.ToArray();
        Assert.Single(installations);
        Assert.Equal("alice", installations[0].OwnerLogin);
        Assert.Equal("USER", installations[0].OwnerType);
        Assert.Equal("ACTIVE", installations[0].Status);
        Assert.True(installations[0].AllRepos);
        Assert.Empty(installations[0].Repos);
    }

    [Fact]
    public async Task GetMine_Unauthenticated_Returns401()
    {
        // Arrange — без auth header.
        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/assignment-review/installations/me/");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class EnvelopeStub<T>
    {
        public T? Result { get; set; }

        public bool IsError { get; set; }
    }
}
