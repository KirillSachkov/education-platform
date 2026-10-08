using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Projects;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features.ProjectItems;

/// <summary>
///     Cross-author ownership rejection coverage for project mutation endpoints.
///     Locks in the <c>UserScopedData.CheckOwnership(project.AuthorId)</c> checks
///     introduced in the ECS audit (issue #259).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class ProjectOwnershipTests : EducationContentServiceTestsBase
{
    public ProjectOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── PATCH /projects/{id} ──────────────────────────────────────────────

    [Fact]
    public async Task UpdateProject_AuthorAUpdatingAuthorBsProject_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid projectId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, authorB, "b-update", ct));

        AuthenticateAs(authorA, "platform-author");
        var request = new UpdateProjectRequest("Hijacked Title", "Hijacked description");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/projects/{projectId}", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateProject_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid projectId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, ownerId, "owner-update", ct));

        AuthenticateAs(ownerId, "platform-author");
        var request = new UpdateProjectRequest("Renamed", "Renamed description");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/projects/{projectId}", request, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task UpdateProject_Admin_BypassesOwnership_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid foreignAuthor = Guid.NewGuid();
        Guid projectId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, foreignAuthor, "admin-update", ct));

        AuthenticateAsAdmin();
        var request = new UpdateProjectRequest("Admin renamed", "Admin description");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/projects/{projectId}", request, ct);

        response.EnsureSuccessStatusCode();
    }

    // ── POST /projects/{id}/issues/{issueId}/attach ───────────────────────

    [Fact]
    public async Task AttachIssueToProject_AuthorAOnAuthorBsProject_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        // Project belongs to author B; issue is orphan-of-A (its own parent project owned by A).
        Guid projectBId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, authorB, "b-attach", ct));
        Guid projectAId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, authorA, "a-source", ct));
        Guid issueAId = await ExecuteInDb(db => OwnershipTestSeed.CreateIssueAsync(db, authorA, projectAId, "a-source-issue", ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/projects/{projectBId}/issues/{issueAId}/attach", content: null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttachIssueToProject_AuthorAAttachingAuthorBsIssueToOwnProject_Returns403()
    {
        // Project owned by A but the issue belongs to B → second ownership check should reject.
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid projectAId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, authorA, "a-target", ct));
        Guid projectBId = await ExecuteInDb(db => OwnershipTestSeed.CreateProjectAsync(db, authorB, "b-source", ct));
        Guid issueBId = await ExecuteInDb(db => OwnershipTestSeed.CreateIssueAsync(db, authorB, projectBId, "b-source-issue", ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/projects/{projectAId}/issues/{issueBId}/attach", content: null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
