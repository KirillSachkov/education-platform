using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EducationContentService.IntegrationTests.Features.CourseItems;

/// <summary>
///     Cross-author ownership rejection coverage for course-item mutation endpoints.
///     Locks in the <c>UserScopedData.CheckOwnership(course.AuthorId)</c> checks
///     introduced in the ECS audit (issue #259).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class CourseItemOwnershipTests : EducationContentServiceTestsBase
{
    public CourseItemOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── POST /courses/{id}/modules ────────────────────────────────────────

    [Fact]
    public async Task CreateModule_AuthorAOnAuthorBsCourse_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "b-create-module", ct));

        AuthenticateAs(authorA, "platform-author");
        var request = new CreateCourseModuleRequest("Hijacked Module", "Should not be created");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateModule_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, ownerId, "owner-create-module", ct));

        AuthenticateAs(ownerId, "platform-author");
        var request = new CreateCourseModuleRequest("Owner's Module", "Owner description");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", request, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateModule_Admin_BypassesOwnership_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid foreignAuthor = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, foreignAuthor, "admin-create-module", ct));

        AuthenticateAsAdmin();
        var request = new CreateCourseModuleRequest("Admin Module", "Admin can do this");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", request, ct);

        response.EnsureSuccessStatusCode();
    }

    // ── PATCH /courses/{courseId}/items/{referenceId}/move ────────────────

    [Fact]
    public async Task MoveCourseItem_AuthorAOnAuthorBsCourse_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        // Two modules in B's course so we have a real CourseItem to move.
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "b-move", ct));
        Guid module1Id = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, authorB, "b-mod-1", published: false, ct));
        Guid module2Id = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, authorB, "b-mod-2", published: false, ct));
        await ExecuteInDb(db => OwnershipTestSeed.AttachItemToCourseAsync(db, courseId, CourseItemType.Module, module1Id, ct));
        await ExecuteInDb(db => OwnershipTestSeed.AttachItemToCourseAsync(db, courseId, CourseItemType.Module, module2Id, ct));

        AuthenticateAs(authorA, "platform-author");
        var request = new MoveCourseItemRequest(AfterSortKey: null, BeforeSortKey: "a0");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/items/{module2Id}/move", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoveCourseItem_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();

        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, ownerId, "owner-move", ct));
        Guid module1Id = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, ownerId, "owner-mod-1", published: false, ct));
        Guid module2Id = await ExecuteInDb(db => OwnershipTestSeed.CreateModuleAsync(db, ownerId, "owner-mod-2", published: false, ct));
        await ExecuteInDb(db => OwnershipTestSeed.AttachItemToCourseAsync(db, courseId, CourseItemType.Module, module1Id, ct));
        await ExecuteInDb(db => OwnershipTestSeed.AttachItemToCourseAsync(db, courseId, CourseItemType.Module, module2Id, ct));

        // Read the first item's sort key so we can move the second one before it.
        string firstSortKey = await ExecuteInDb(async db =>
            await db.CourseItems
                .Where(ci => ci.CourseId == courseId && ci.ReferenceId == module1Id)
                .Select(ci => ci.SortKey.Value)
                .FirstAsync(ct));

        AuthenticateAs(ownerId, "platform-author");
        var request = new MoveCourseItemRequest(AfterSortKey: null, BeforeSortKey: firstSortKey);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/items/{module2Id}/move", request, ct);

        response.EnsureSuccessStatusCode();
    }
}
