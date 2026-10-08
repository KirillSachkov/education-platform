using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Digest;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Digest;

/// <summary>
///     <c>GET /internal/digest/content</c> (#532) — глобальный контент еженедельного
///     дайджеста. Проверяем: роль SERVICE/ADMIN, окно по published_at, primary course-slug
///     у привязанного материала / null у standalone, исключение DRAFT и старого контента,
///     PUBLISHED-курсы за окно.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetDigestContentTests : EducationContentServiceTestsBase
{
    public GetDigestContentTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Endpoint_Anonymous_Rejected()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            DigestUrl(DateTime.UtcNow.AddDays(-7)));

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task Endpoint_ReturnsWindowedMaterialsWithCourseSlug_AndCourses()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        // Свежий материал, привязанный к PUBLISHED-курсу.
        Guid courseId = await CreatePublishedCourseAsync(authorId, slug: "dotnet-digest", ct);
        Guid boundMaterialId = await CreatePublishedMaterialAsync("Дженерики в C#", authorId, ct);
        await AttachMaterialToCourseAsync(courseId, boundMaterialId, ct);

        // Свежий standalone материал — CourseSlug = null.
        Guid standaloneMaterialId = await CreatePublishedMaterialAsync("Span и память", authorId, ct);

        // Старый материал — published_at за пределами окна.
        Guid oldMaterialId = await CreatePublishedMaterialAsync("Старый материал", authorId, ct);
        await SetMaterialPublishedAtAsync(oldMaterialId, DateTime.UtcNow.AddDays(-10), ct);

        // DRAFT-материал — не попадает.
        await CreateDraftMaterialAsync("Черновик", authorId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            DigestUrl(DateTime.UtcNow.AddDays(-7)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<DigestContentDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<DigestContentDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        DigestContentDto digest = envelope.Result;

        Assert.Equal(2, digest.Materials.Count);
        DigestMaterialDto bound = Assert.Single(digest.Materials, m => m.MaterialId == boundMaterialId);
        Assert.Equal("Дженерики в C#", bound.Title);
        Assert.Equal("dotnet-digest", bound.CourseSlug);

        DigestMaterialDto standalone = Assert.Single(digest.Materials, m => m.MaterialId == standaloneMaterialId);
        Assert.Null(standalone.CourseSlug);
        Assert.Null(standalone.CourseTitle);

        // Курс, опубликованный «на этой неделе», попадает в courses.
        DigestCourseDto course = Assert.Single(digest.Courses, c => c.CourseId == courseId);
        Assert.Equal("dotnet-digest", course.Slug);
        Assert.Equal("COURSE", course.Kind);
    }

    [Fact]
    public async Task Endpoint_OldCourse_NotReturned()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid oldCourseId = await CreatePublishedCourseAsync(authorId, slug: "old-course", ct);
        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE education.courses SET published_at = {DateTime.UtcNow.AddDays(-30)} WHERE id = {oldCourseId}"));

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            DigestUrl(DateTime.UtcNow.AddDays(-7)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<DigestContentDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<DigestContentDto>>();
        Assert.NotNull(envelope?.Result);
        Assert.DoesNotContain(envelope.Result.Courses, c => c.CourseId == oldCourseId);
    }

    // -- helpers --

    private static Uri DigestUrl(DateTime sinceUtc) =>
        new($"/internal/digest/content?sinceUtc={Uri.EscapeDataString(sinceUtc.ToString("O"))}&maxItems=20",
            UriKind.Relative);

    private async Task<Guid> CreatePublishedMaterialAsync(string title, Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task CreateDraftMaterialAsync(string title, Guid authorId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.ARTICLE, AccessType.PUBLIC);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
        });
    }

    private async Task SetMaterialPublishedAtAsync(Guid materialId, DateTime publishedAt, CancellationToken ct)
    {
        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE education.materials SET published_at = {publishedAt} WHERE id = {materialId}"));
    }

    private async Task<Guid> CreatePublishedCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
            Assert.True(course.Publish().IsSuccess);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task AttachMaterialToCourseAsync(Guid courseId, Guid materialId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseMaterials.Add(new CourseMaterial(courseId, materialId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });
    }
}
