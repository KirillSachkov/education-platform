using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using FileService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Ordering;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.IntegrationTests.Features.Materials;

/// <summary>
///     Course-aware ownership for the material edit surface (#657). A course owner must be able
///     to view and manage materials placed in their course even when another author (e.g. an admin
///     who added the material) is the material's author. Before the fix the material detail/edit
///     endpoints scoped access by <c>material.author_id</c> only, so the course owner got a 404
///     ("элемент не найден") on a draft material added by someone else. Admin / content-moderator
///     keep their documented Tier-2 bypass; outsiders and the student entitlement gate are unchanged.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class MaterialCourseOwnerEditTests : EducationContentServiceTestsBase
{
    private readonly IntegrationTestsWebFactory _factory;

    public MaterialCourseOwnerEditTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    // ---- Read: GET /materials/{id}/detail ----

    [Fact] // AC1 — the reported bug
    public async Task GetDetail_DraftMaterialAddedByAdmin_CourseOwnerLoadsFullContent_200()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll(); // course owner must pass via the manager short-circuit, not entitlement
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(
            materialAuthor: admin, courseOwner: courseOwner, "это тело урока по DevOps", ct);

        AuthenticateAs(courseOwner, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MaterialDetailDto dto = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Equal("это тело урока по DevOps", dto.Content);
    }

    [Fact] // AC3
    public async Task GetDetail_DraftMaterial_Admin_200()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "draft body", ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // AC3
    public async Task GetDetail_DraftMaterial_Moderator_200()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "draft body", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // AC4 — outsider author owning neither the material nor a containing course
    public async Task GetDetail_DraftMaterialInOtherAuthorsCourse_Outsider_404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-author"); // unrelated author
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // AC4 — no draft leak to anonymous
    public async Task GetDetail_DraftMaterial_Anonymous_404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(admin, courseOwner, "body", ct);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // AC5 — standalone draft stays author-only for non-managers
    public async Task GetDetail_StandaloneDraft_NonAuthorAuthor_404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "body", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact] // AC5 — author still loads their own standalone draft (regression)
    public async Task GetDetail_StandaloneDraft_Author_200()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "body", ct);

        AuthenticateAs(materialAuthor, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact] // AC4 regression — student entitlement gate on PUBLISHED gated content is untouched
    public async Task GetDetail_PublishedEnrolledMaterial_OutsiderNotEntitled_403()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedPublishedMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-participant"); // a student, not the owner, not entitled
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact] // AC4 regression — content-moderator does NOT get free Tier-3 access to PUBLISHED paid content
    public async Task GetDetail_PublishedEnrolledMaterial_ModeratorNotEntitled_403()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedPublishedMaterialInCourseAsync(admin, courseOwner, "body", ct);

        // moderate bypasses Tier-2 ownership (sees DRAFTs) but Tier-3 entitlement still applies
        AuthenticateAs(Guid.NewGuid(), "platform-moderator");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact] // course owner bypasses Tier-3 even on PUBLISHED gated material in their course
    public async Task GetDetail_PublishedEnrolledMaterial_CourseOwner_200()
    {
        CancellationToken ct = CancellationToken.None;
        EntitlementChecker.DenyAll();
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedPublishedMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(courseOwner, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- Write: PATCH / publish / archive / draft ----

    [Fact] // AC2
    public async Task UpdateMaterial_DraftAddedByAdmin_CourseOwner_200_Persisted()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(admin, courseOwner, "old body", ct);

        AuthenticateAs(courseOwner, "platform-author");
        var request = new UpdateMaterialRequest(
            Title: "Отредактировано владельцем курса",
            Content: "новое тело",
            Kind: "ARTICLE",
            AccessType: "ENROLLED");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", request, ct);

        response.EnsureSuccessStatusCode();
        string title = await ExecuteInDb(db => db.Materials
            .Where(m => m.Id == materialId).Select(m => m.Title.Value).FirstAsync(ct));
        Assert.Equal("Отредактировано владельцем курса", title);
    }

    [Fact]
    public async Task UpdateMaterial_CourseOwnerCanRemoveMediaUploadedByAnotherManager()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialAuthor = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        Guid videoId = Guid.CreateVersion7();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(
            materialAuthor,
            courseOwner,
            "old body",
            ct);
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.SingleAsync(item => item.Id == materialId, ct);
            material.AttachVideo(VideoId.Create(videoId).Value);
            await db.SaveChangesAsync(ct);
        });
        IFileServiceClient fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.ClearReceivedCalls();

        AuthenticateAs(courseOwner, "platform-author");
        var request = new UpdateMaterialRequest(
            Title: "Без старого видео",
            Content: "новое тело",
            Kind: "ARTICLE",
            AccessType: "ENROLLED",
            VideoId: null);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/materials/{materialId}",
            request,
            ct);

        response.EnsureSuccessStatusCode();
        FileAssetDetached detached = Assert.Single(_factory.OutboxCollector.OfType<FileAssetDetached>());
        Assert.Equal(videoId, detached.AssetId);
        Assert.Equal(0, detached.ExpectedBindingRevision);
    }

    [Fact] // AC2
    public async Task PublishMaterial_DraftAddedByAdmin_CourseOwner_200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(courseOwner, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/materials/{materialId}/publish", new { NotifySubscribers = false }, ct);

        response.EnsureSuccessStatusCode();
        PublicationStatus status = await GetStatusAsync(materialId, ct);
        Assert.Equal(PublicationStatus.PUBLISHED, status);
    }

    [Fact] // AC2
    public async Task ArchiveMaterial_PublishedInOwnedCourse_CourseOwner_200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedPublishedMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(courseOwner, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsync($"/materials/{materialId}/archive", null, ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal(PublicationStatus.ARCHIVED, await GetStatusAsync(materialId, ct));
    }

    [Fact] // AC2
    public async Task SendToDraft_PublishedInOwnedCourse_CourseOwner_200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedPublishedMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(courseOwner, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsync($"/materials/{materialId}/draft", null, ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal(PublicationStatus.DRAFT, await GetStatusAsync(materialId, ct));
    }

    [Fact] // AC3 — admin manages any material
    public async Task UpdateMaterial_DraftAnyCourse_Admin_200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "old", ct);

        AuthenticateAsAdmin();
        var request = new UpdateMaterialRequest(
            Title: "Admin edit", Content: "x", Kind: "ARTICLE", AccessType: "PUBLIC");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", request, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact] // AC4 — outsider cannot edit a material outside their courses
    public async Task UpdateMaterial_DraftNotInCallersCourse_Outsider_403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid admin = Guid.NewGuid();
        Guid courseOwner = Guid.NewGuid();
        (_, Guid materialId) = await SeedDraftMaterialInCourseAsync(admin, courseOwner, "body", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-author"); // owns neither material nor course
        var request = new UpdateMaterialRequest(
            Title: "Hijack", Content: "x", Kind: "ARTICLE", AccessType: "ENROLLED");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact] // AC5 — standalone draft stays author-only for writes
    public async Task UpdateMaterial_StandaloneDraft_NonAuthorAuthor_403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialAuthor = Guid.NewGuid();
        Guid materialId = await SeedStandaloneDraftMaterialAsync(materialAuthor, "old", ct);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        var request = new UpdateMaterialRequest(
            Title: "Hijack", Content: "x", Kind: "ARTICLE", AccessType: "PUBLIC");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync($"/materials/{materialId}", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- seeding helpers ----

    private Task<(Guid CourseId, Guid MaterialId)> SeedDraftMaterialInCourseAsync(
        Guid materialAuthor, Guid courseOwner, string content, CancellationToken ct)
        => SeedMaterialInCourseAsync(materialAuthor, courseOwner, content, publish: false, ct);

    private Task<(Guid CourseId, Guid MaterialId)> SeedPublishedMaterialInCourseAsync(
        Guid materialAuthor, Guid courseOwner, string content, CancellationToken ct)
        => SeedMaterialInCourseAsync(materialAuthor, courseOwner, content, publish: true, ct);

    private async Task<(Guid CourseId, Guid MaterialId)> SeedMaterialInCourseAsync(
        Guid materialAuthor, Guid courseOwner, string content, bool publish, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        Guid materialId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            string slug = $"co-{Guid.NewGuid():N}"[..16];
            var course = new Course(
                courseOwner,
                Title.Create("Курс по DevOps").Value,
                Description.Create("Описание").Value,
                CourseSlug.Create(slug).Value,
                SortKey.Initial());
            db.Courses.Add(course);

            Material material = BuildMaterial(materialAuthor, content, publish);
            db.Materials.Add(material);
            db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, SortKey.Initial()));

            await db.SaveChangesAsync(ct);
            courseId = course.Id;
            materialId = material.Id;
        });
        return (courseId, materialId);
    }

    private async Task<Guid> SeedStandaloneDraftMaterialAsync(
        Guid materialAuthor, string content, CancellationToken ct)
    {
        Guid materialId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Material material = BuildMaterial(materialAuthor, content, publish: false);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }

    private static Material BuildMaterial(Guid authorId, string content, bool publish)
    {
        var material = new Material(
            authorId, Title.Create("Урок").Value, MaterialKind.ARTICLE, AccessType.ENROLLED);
        material.Update(
            Title.Create("Урок").Value,
            MarkdownContent.Create(content).Value,
            MaterialKind.ARTICLE,
            AccessType.ENROLLED,
            boundCourseCount: 0,
            description: null);
        if (publish)
            Assert.True(material.Publish().IsSuccess);
        return material;
    }

    private Task<PublicationStatus> GetStatusAsync(Guid materialId, CancellationToken ct)
        => ExecuteInDb(db => db.Materials
            .Where(m => m.Id == materialId).Select(m => m.Status).FirstAsync(ct));
}
