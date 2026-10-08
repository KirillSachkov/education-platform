using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Modules;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features.Modules;

/// <summary>
///     Cross-author ownership rejection coverage for module mutation endpoints.
///     Locks in the <c>UserScopedData.CheckOwnership(module.AuthorId)</c> checks
///     introduced in the ECS audit (issue #259) so regressions are caught.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class ModuleOwnershipTests : EducationContentServiceTestsBase
{
    public ModuleOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── POST /modules/{id}/publish ────────────────────────────────────────

    [Fact]
    public async Task PublishModule_AuthorAPublishingAuthorBsModule_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, authorB, "b-publish", published: false, ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/modules/{moduleId}/publish", content: null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PublishModule_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, ownerId, "owner-publish", published: false, ct));

        AuthenticateAs(ownerId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/modules/{moduleId}/publish", content: null, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PublishModule_Admin_BypassesOwnership_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid foreignAuthorId = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, foreignAuthorId, "admin-publish", published: false, ct));

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/modules/{moduleId}/publish", content: null, ct);

        response.EnsureSuccessStatusCode();
    }

    // ── PATCH /modules/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task UpdateModule_AuthorAUpdatingAuthorBsModule_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, authorB, "b-update", published: false, ct));

        AuthenticateAs(authorA, "platform-author");
        var request = new UpdateModuleRequest("Hijacked Title", "Hijacked Description");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/modules/{moduleId}", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateModule_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, ownerId, "owner-update", published: false, ct));

        AuthenticateAs(ownerId, "platform-author");
        var request = new UpdateModuleRequest("Renamed", "Renamed description");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/modules/{moduleId}", request, ct);

        response.EnsureSuccessStatusCode();
    }

    // ── POST /modules/{id}/archive ────────────────────────────────────────

    [Fact]
    public async Task ArchiveModule_AuthorAArchivingAuthorBsModule_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, authorB, "b-archive", published: true, ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/modules/{moduleId}/archive", content: null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ArchiveModule_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid moduleId = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, ownerId, "owner-archive", published: true, ct));

        AuthenticateAs(ownerId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync($"/modules/{moduleId}/archive", content: null, ct);

        response.EnsureSuccessStatusCode();
    }
}
