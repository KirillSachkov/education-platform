using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.Installations;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using PlatformAuth.Authorization;

namespace AssignmentReviewService.IntegrationTests.Features.Admin;

/// <summary>
///     Tests for <c>GET /assignment-review/admin/users/{userId}/installations/</c> (#444) —
///     admin/support GitHub App snapshot for the post-purchase panel. Verifies it returns the
///     TARGET user's installations (not the caller's), and the ADMIN/MODERATOR gate.
/// </summary>
public sealed class GetUserInstallationsTests : AssignmentReviewServiceTestsBase
{
    public GetUserInstallationsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GetForUser_ModeratorCaller_ReturnsTargetUsersInstallations()
    {
        // Arrange — installations for the support target + an unrelated user.
        Guid target = Guid.NewGuid();
        Guid other = Guid.NewGuid();

        VcsInstallation targetInstall = VcsInstallation.Create(
            VcsProvider.GITHUB,
            installationId: "3003",
            VcsInstallationOwnerType.USER,
            ownerLogin: "buyer-gh",
            ownerExternalId: "300",
            linkedUserId: target,
            RepoSelections.AllRepos());

        VcsInstallation alienInstall = VcsInstallation.Create(
            VcsProvider.GITHUB,
            installationId: "4004",
            VcsInstallationOwnerType.ORG,
            ownerLogin: "acme-co",
            ownerExternalId: "400",
            linkedUserId: other,
            RepoSelections.Specific(["acme-co/repo1"]));

        await ExecuteInDbAsync(async db =>
        {
            db.VcsInstallations.Add(targetInstall);
            db.VcsInstallations.Add(alienInstall);
            await db.SaveChangesAsync();
        });

        // Caller is a moderator with their own id — endpoint reads the route's userId, not caller's.
        AuthenticateAs(PlatformRoles.MODERATOR);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/admin/users/{target}/installations/");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvelopeStub<GetMyInstallationsResponse>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);

        VcsInstallationDto[] installations = body.Result!.Installations.ToArray();
        Assert.Single(installations);
        Assert.Equal("buyer-gh", installations[0].OwnerLogin);
        Assert.Equal("USER", installations[0].OwnerType);
        Assert.Equal("ACTIVE", installations[0].Status);
        Assert.True(installations[0].AllRepos);
    }

    [Fact]
    public async Task GetForUser_NoInstallations_ReturnsEmptyList()
    {
        AuthenticateAs(PlatformRoles.ADMIN);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/admin/users/{Guid.NewGuid()}/installations/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<EnvelopeStub<GetMyInstallationsResponse>>();
        Assert.NotNull(body);
        Assert.False(body!.IsError);
        Assert.Empty(body.Result!.Installations);
    }

    [Fact]
    public async Task GetForUser_NonAdminCaller_ReturnsForbidden()
    {
        AuthenticateAs(PlatformRoles.AUTHOR);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/admin/users/{Guid.NewGuid()}/installations/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetForUser_Unauthenticated_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/admin/users/{Guid.NewGuid()}/installations/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class EnvelopeStub<T>
    {
        public T? Result { get; set; }

        public bool IsError { get; set; }
    }
}
