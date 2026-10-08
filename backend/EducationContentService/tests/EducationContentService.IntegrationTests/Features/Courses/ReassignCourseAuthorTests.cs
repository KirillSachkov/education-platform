using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Course ownership transfer (#587). An admin / content-moderator reassigns a course AND
///     the content owned exclusively by it (modules, materials, …) to a new author. The new
///     author then owns and can manage it; the previous author loses management. Materials/
///     quizzes shared with other courses are kept under the previous author. Student access
///     is NOT touched (no access events) — it stays plan-driven and author-agnostic (#589).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ReassignCourseAuthorTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public ReassignCourseAuthorTests(IntegrationTestsWebFactory factory) : base(factory) =>
        _factory = factory;

    [Fact]
    public async Task Transfer_flipsCourseAndExclusivelyOwnedContent_toNewAuthor()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);
        Guid moduleId = await SeedModuleInCourseAsync(authorA, courseId, ct);
        Guid materialId = await CreateMaterialInCourseAsync(authorA, courseId, marker, ct);

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        ReassignCourseAuthorResponse body = await ReadResultAsync<ReassignCourseAuthorResponse>(resp);
        Assert.Equal(authorB, body.NewAuthorId);
        Assert.Empty(body.SkippedSharedMaterialIds);

        await ExecuteInDb(async db =>
        {
            Assert.Equal(authorB, await AuthorOfCourse(db, courseId, ct));
            Assert.Equal(authorB, await AuthorOfModule(db, moduleId, ct));
            Assert.Equal(authorB, await AuthorOfMaterial(db, materialId, ct));
        });
    }

    [Fact]
    public async Task Transfer_publishesDurableAssetOwnershipSnapshot_withoutSyncFileWrite()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        string marker = Marker();
        IFileServiceClient fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);
        Guid materialId = await CreateMaterialInCourseAsync(authorA, courseId, marker, ct);
        _factory.OutboxCollector.Clear();

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await fileClient.DidNotReceive().ReassignAssetsOwnerAsync(
            Arg.Any<ReassignAssetOwnerRequest>(),
            Arg.Any<CancellationToken>());

        CourseAssetOwnershipChanged ownership =
            Assert.Single(_factory.OutboxCollector.OfType<CourseAssetOwnershipChanged>());
        Assert.Equal(courseId, ownership.CourseId);
        Assert.Equal(authorB, ownership.NewOwnerId);
        Assert.True(ownership.OwnershipRevision > 0);
        Assert.Contains(ownership.Targets, t => t.Type == "course" && t.Id == courseId);
        Assert.Contains(ownership.Targets, t => t.Type == "material" && t.Id == materialId);
    }

    [Fact]
    public async Task Transfer_keepsSharedMaterialWithPreviousAuthor_andReportsIt()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        string marker = Marker();

        Guid courseOne = await CreateCourseAsAsync(authorA, marker + "1", ct);
        Guid courseTwo = await CreateCourseAsAsync(authorA, marker + "2", ct);
        Guid sharedMaterialId = await CreateMaterialInCourseAsync(authorA, courseOne, marker, ct);

        // Attach the same material to a second course → it is now shared.
        await ExecuteInDb(async db =>
        {
            db.CourseMaterials.Add(new CourseMaterial(courseTwo, sharedMaterialId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseOne}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        ReassignCourseAuthorResponse body = await ReadResultAsync<ReassignCourseAuthorResponse>(resp);
        Assert.Contains(sharedMaterialId, body.SkippedSharedMaterialIds);

        // Shared material stays with the previous author (course two is untouched).
        await ExecuteInDb(async db =>
        {
            Assert.Equal(authorB, await AuthorOfCourse(db, courseOne, ct));
            Assert.Equal(authorA, await AuthorOfMaterial(db, sharedMaterialId, ct));
        });
    }

    [Fact]
    public async Task Transfer_movesManagement_newAuthorCanEdit_previousCannot()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);

        AuthenticateAsAdmin();
        HttpResponseMessage transfer = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        // New author manages it.
        AuthenticateAs(authorB, "platform-author");
        HttpResponseMessage byNew = await PatchAsJsonAsync(
            $"/courses/{courseId}", new UpdateCourseRequest($"Owned by B {marker}", "desc"));
        Assert.Equal(HttpStatusCode.OK, byNew.StatusCode);

        // Previous author no longer can (not owner, no content.moderate).
        AuthenticateAs(authorA, "platform-author");
        HttpResponseMessage byOld = await PatchAsJsonAsync(
            $"/courses/{courseId}", new UpdateCourseRequest($"Owned by A {marker}", "desc"));
        Assert.Equal(HttpStatusCode.Forbidden, byOld.StatusCode);
    }

    [Fact]
    public async Task Transfer_adminStillSeesCourseInManagementList()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid adminId = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);

        AuthenticateAsAdmin(adminId);
        HttpResponseMessage transfer = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, transfer.StatusCode);

        AuthenticateAsAdmin(adminId);
        HttpResponseMessage list = await AppHttpClient.GetAsync("/courses/my?limit=20", ct);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        CursorResponse<CourseSummaryDto> body =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(list);
        Assert.Contains(body.Items, c => c.Id == courseId && c.AuthorId == authorB);
    }

    [Fact]
    public async Task Transfer_requiresContentModerate_403ForPlainAuthor()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);

        // The owning author (platform-author, no content.moderate) cannot transfer it away.
        AuthenticateAs(authorA, "platform-author");
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_toSameAuthor_isRejected()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorA));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Transfer_doesNotTouchAccess_emitsNoAccessChangedEvents()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        string marker = Marker();

        Guid courseId = await CreateCourseAsAsync(authorA, marker, ct);
        await CreateMaterialInCourseAsync(authorA, courseId, marker, ct);

        // Seeding above publishes material events; reset so we only observe the transfer.
        _factory.OutboxCollector.Clear();

        AuthenticateAsAdmin();
        HttpResponseMessage resp = await PatchAsJsonAsync(
            $"/courses/{courseId}/author", new ReassignCourseAuthorRequest(authorB));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        // Ownership-only change: access tags are left intact, so no access events fire.
        Assert.Empty(_factory.OutboxCollector.OfType<MaterialAccessChanged>());
        Assert.Empty(_factory.OutboxCollector.OfType<CollectionAccessChanged>());
    }

    private static string Marker() => Guid.NewGuid().ToString("N")[..8];

    private async Task<Guid> CreateCourseAsAsync(Guid authorId, string marker, CancellationToken ct)
    {
        AuthenticateAs(authorId, "platform-author");
        var req = new { Title = $"Course {marker}", Description = "course description", Slug = $"course-{marker}" };
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/courses", req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<Guid>(resp);
    }

    private async Task<Guid> CreateMaterialInCourseAsync(
        Guid authorId, Guid courseId, string marker, CancellationToken ct)
    {
        AuthenticateAs(authorId, "platform-author");
        var req = new CreateMaterialRequest(
            Title: $"Material {marker}-{Guid.NewGuid():N}",
            Content: "material body",
            Kind: "ARTICLE",
            AccessType: "ENROLLED",
            CourseId: courseId);
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/materials", req, ct);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await ReadResultAsync<Guid>(resp);
    }

    private async Task<Guid> SeedModuleInCourseAsync(Guid authorId, Guid courseId, CancellationToken ct)
    {
        Guid moduleId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(authorId, Title.Create($"Module {Guid.NewGuid():N}").Value);
            db.Modules.Add(module);
            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, module.Id, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
            moduleId = module.Id;
        });
        return moduleId;
    }

    private static async Task<Guid> AuthorOfCourse(
        EducationContentService.Infrastructure.Postgres.EducationDbContext db, Guid id, CancellationToken ct) =>
        await db.Courses.AsNoTracking().Where(c => c.Id == id).Select(c => c.AuthorId).FirstAsync(ct);

    private static async Task<Guid> AuthorOfModule(
        EducationContentService.Infrastructure.Postgres.EducationDbContext db, Guid id, CancellationToken ct) =>
        await db.Modules.AsNoTracking().Where(m => m.Id == id).Select(m => m.AuthorId).FirstAsync(ct);

    private static async Task<Guid> AuthorOfMaterial(
        EducationContentService.Infrastructure.Postgres.EducationDbContext db, Guid id, CancellationToken ct) =>
        await db.Materials.AsNoTracking().Where(m => m.Id == id).Select(m => m.AuthorId).FirstAsync(ct);
}
