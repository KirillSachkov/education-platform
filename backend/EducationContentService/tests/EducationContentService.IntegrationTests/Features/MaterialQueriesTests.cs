using System.Net;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class MaterialQueriesTests : EducationContentServiceTestsBase
{
    public MaterialQueriesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetMaterialDetail_ExistingPublished_ReturnsData()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Публичный материал", "# Body", authorId, AccessType.PUBLIC, ct);

        Guid userId = Guid.CreateVersion7();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.GrantAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        response.EnsureSuccessStatusCode();

        MaterialDetailDto dto = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Equal(materialId, dto.Id);
        Assert.Equal("Публичный материал", dto.Title);
        Assert.Equal("# Body", dto.Content);
        Assert.Equal(MaterialKind.ARTICLE.ToString(), dto.Kind);
        Assert.Equal("PUBLISHED", dto.Status);
    }

    [Fact]
    public async Task GetMaterialDetail_ByAnonymous_PublicMaterial_ReturnsData()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Open material", "# Body", authorId, AccessType.PUBLIC, ct);

        // Анонимный клиент — без токена.
        RemoveAuthentication();
        // Для PUBLIC-материалов Redis-теги пустые, FakeEntitlementChecker в grant-all режиме
        // симулирует поведение настоящего checker'а: анонимный доступ разрешён.
        EntitlementChecker.GrantAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        response.EnsureSuccessStatusCode();

        MaterialDetailDto dto = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Equal(materialId, dto.Id);
        Assert.True(dto.IsAccessible);
    }

    [Fact]
    public async Task GetMaterialDetail_ByAnonymous_EnrolledMaterial_ReturnsAccessDenied()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Paid material", "# Body", authorId, AccessType.ENROLLED, ct);

        RemoveAuthentication();
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialDetail_ByAuthenticatedUser_WithoutEntitlement_ReturnsForbidden()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Paid material", "# Body", authorId, AccessType.ENROLLED, ct);

        Guid userId = Guid.CreateVersion7();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialDetail_VideoMaterialWithChapters_ReturnsChaptersSorted()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        // Chapters в произвольном порядке — handler должен отсортировать по timeSeconds.
        Guid materialId = await CreatePublishedVideoMaterialWithChaptersInDb(
            "Видео с главами",
            authorId,
            chapterTitles: ["Третья", "Первая", "Вторая"],
            chapterTimestamps: [120, 0, 60],
            ct);

        Guid userId = Guid.CreateVersion7();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.GrantAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        response.EnsureSuccessStatusCode();

        MaterialDetailDto dto = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Equal(3, dto.Chapters.Count);
        Assert.Equal("Первая", dto.Chapters[0].Title);
        Assert.Equal(0, dto.Chapters[0].TimeSeconds);
        Assert.Equal("Вторая", dto.Chapters[1].Title);
        Assert.Equal(60, dto.Chapters[1].TimeSeconds);
        Assert.Equal("Третья", dto.Chapters[2].Title);
        Assert.Equal(120, dto.Chapters[2].TimeSeconds);
    }

    [Fact]
    public async Task GetMaterialDetail_ArticleWithoutChapters_ReturnsEmptyChapters()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Статья", "# Body", authorId, AccessType.PUBLIC, ct);

        Guid userId = Guid.CreateVersion7();
        AuthenticateAs(userId, "platform-participant");
        EntitlementChecker.GrantAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        response.EnsureSuccessStatusCode();

        MaterialDetailDto dto = await ReadResultAsync<MaterialDetailDto>(response);
        Assert.Empty(dto.Chapters);
    }

    [Fact]
    public async Task GetMaterialDetail_ByAnonymous_RegisteredMaterial_ReturnsUnauthorized()
    {
        // Regression: ранее анонимы получали доступ, если Redis-тег отсутствовал (fail-open).
        // Теперь checker всегда требует явный тег; REGISTERED-материал → NOT_AUTHENTICATED.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Registered-only", "# Body", authorId, AccessType.REGISTERED, ct);

        RemoveAuthentication();
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialPreview_Anonymous_PublicMaterial_ReturnsMetadata()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Public preview", "# Body", authorId, AccessType.PUBLIC, ct);

        RemoveAuthentication();
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/preview", ct);

        response.EnsureSuccessStatusCode();

        MaterialPreviewDto dto = await ReadResultAsync<MaterialPreviewDto>(response);
        Assert.Equal(materialId, dto.Id);
        Assert.Equal("Public preview", dto.Title);
        Assert.Equal(MaterialKind.ARTICLE.ToString(), dto.Kind);
        Assert.Equal(AccessType.PUBLIC.ToString(), dto.AccessType);
    }

    [Fact]
    public async Task GetMaterialPreview_Anonymous_EnrolledMaterial_ReturnsMetadataRegardlessOfEntitlement()
    {
        // OG-карточки шарятся для гейтнутого контента тоже — title + cover считаются
        // promotional metadata, body наружу не утекает (preview endpoint его не возвращает).
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb(
            "Gated preview", "# Body", authorId, AccessType.ENROLLED, ct);

        RemoveAuthentication();
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/preview", ct);

        response.EnsureSuccessStatusCode();

        MaterialPreviewDto dto = await ReadResultAsync<MaterialPreviewDto>(response);
        Assert.Equal("Gated preview", dto.Title);
        Assert.Equal(AccessType.ENROLLED.ToString(), dto.AccessType);
    }

    [Fact]
    public async Task GetMaterialPreview_DraftMaterial_ReturnsNotFound()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Draft", "# Body", authorId, ct);

        RemoveAuthentication();
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/preview", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterials_WithScopeMine_ReturnsOwnMaterials()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreatePublishedMaterialInDb("Мой первый", "# A", authorId, AccessType.PUBLIC, ct);
        await CreateDraftMaterialInDb("Мой черновик", "# B", authorId, ct);
        await CreatePublishedMaterialInDb("Чужой", "# C", Guid.CreateVersion7(), AccessType.PUBLIC, ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/materials?scope=mine&limit=10", ct);

        response.EnsureSuccessStatusCode();

        CursorResponse<MaterialSummaryDto> page = await ReadResultAsync<CursorResponse<MaterialSummaryDto>>(response);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(authorId, item.AuthorId));
        Assert.Contains(page.Items, i => i.Title == "Мой первый");
        Assert.Contains(page.Items, i => i.Title == "Мой черновик");
    }

    [Fact]
    public async Task GetAuthorMaterials_Published_ReturnsSpaceTimeline()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreatePublishedMaterialInDb("Первый опубликованный", "# A", authorId, AccessType.PUBLIC, ct);
        await CreatePublishedMaterialInDb("Второй опубликованный", "# B", authorId, AccessType.PUBLIC, ct);
        await CreateDraftMaterialInDb("Черновик", "# Draft", authorId, ct);
        await CreatePublishedMaterialInDb("Чужой", "# Other", Guid.CreateVersion7(), AccessType.PUBLIC, ct);

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/materials?limit=10", ct);

        response.EnsureSuccessStatusCode();

        CursorResponse<MaterialSummaryDto> page = await ReadResultAsync<CursorResponse<MaterialSummaryDto>>(response);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(authorId, item.AuthorId));
        Assert.All(page.Items, item => Assert.Equal("PUBLISHED", item.Status));
        Assert.Contains(page.Items, i => i.Title == "Первый опубликованный");
        Assert.Contains(page.Items, i => i.Title == "Второй опубликованный");
    }

    [Fact]
    public async Task GetMaterials_ScopeMine_ReturnsAttachedCourses()
    {
        // Issue #215: picker должен видеть в каких курсах материал уже используется,
        // чтобы автор не дублировал. Проверяем что scope=mine отдаёт массив courses.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid materialId = Guid.Empty;
        Guid courseAId = Guid.Empty;
        Guid courseBId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create("Reused material").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC);
            material.SetContent(MarkdownContent.Create("# Body").Value);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);

            var courseA = new Course(
                authorId,
                Title.Create("Course A").Value,
                Description.Create("Описание A").Value,
                CourseSlug.Create($"a-{Guid.NewGuid():N}".Substring(0, 12)).Value,
                SortKey.Initial());
            var courseB = new Course(
                authorId,
                Title.Create("Course B").Value,
                Description.Create("Описание B").Value,
                CourseSlug.Create($"b-{Guid.NewGuid():N}".Substring(0, 12)).Value,
                SortKey.Initial());
            db.Courses.AddRange(courseA, courseB);

            db.CourseMaterials.AddRange(
                new CourseMaterial(courseA.Id, material.Id, SortKey.Initial()),
                new CourseMaterial(courseB.Id, material.Id, SortKey.Initial()));

            await db.SaveChangesAsync(ct);
            materialId = material.Id;
            courseAId = courseA.Id;
            courseBId = courseB.Id;
        });

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/materials?scope=mine&limit=10", ct);
        response.EnsureSuccessStatusCode();

        CursorResponse<MaterialSummaryDto> page = await ReadResultAsync<CursorResponse<MaterialSummaryDto>>(response);
        MaterialSummaryDto reused = Assert.Single(page.Items, i => i.Id == materialId);
        Assert.Equal(2, reused.Courses.Count);
        Assert.Contains(reused.Courses, c => c.CourseId == courseAId && c.Title == "Course A");
        Assert.Contains(reused.Courses, c => c.CourseId == courseBId && c.Title == "Course B");
    }

    [Fact]
    public async Task GetMaterials_ScopeMine_OrphanMaterial_ReturnsEmptyCoursesArray()
    {
        // Orphan-материал (без course_materials) → пустой массив courses, а не null.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreateDraftMaterialInDb("Orphan", "# Body", authorId, ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/materials?scope=mine&limit=10", ct);
        response.EnsureSuccessStatusCode();

        CursorResponse<MaterialSummaryDto> page = await ReadResultAsync<CursorResponse<MaterialSummaryDto>>(response);
        MaterialSummaryDto orphan = Assert.Single(page.Items);
        Assert.Empty(orphan.Courses);
    }

    private async Task<Guid> CreateDraftMaterialInDb(
        string title,
        string? content,
        Guid authorId,
        CancellationToken ct)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create(title).Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC);

            if (!string.IsNullOrWhiteSpace(content))
            {
                material.Update(
                    Title.Create(title).Value,
                    MarkdownContent.Create(content).Value,
                    MaterialKind.ARTICLE,
                    AccessType.PUBLIC,
                    boundCourseCount: 0,
                    description: null);
            }

            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });

        return materialId;
    }

    private async Task<Guid> CreatePublishedMaterialInDb(
        string title,
        string content,
        Guid authorId,
        AccessType accessType,
        CancellationToken ct)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            int boundCourseCount = RequiresCourseBinding(accessType) ? 1 : 0;
            var material = new Material(
                authorId,
                Title.Create(title).Value,
                MaterialKind.ARTICLE,
                accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create(content).Value,
                MaterialKind.ARTICLE,
                accessType,
                boundCourseCount,
                description: null);
            Assert.True(material.Publish().IsSuccess);

            db.Materials.Add(material);

            if (RequiresCourseBinding(accessType))
            {
                var course = new Course(
                    authorId,
                    Title.Create($"Курс для {title}").Value,
                    Description.Create("Описание").Value,
                    slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value, SortKey.Initial());
                Assert.True(course.Publish().IsSuccess);
                db.Courses.Add(course);
                db.CourseMaterials.Add(new CourseMaterial(course.Id, material.Id, SortKey.Initial()));
            }

            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });

        return materialId;
    }

    private async Task<Guid> CreatePublishedVideoMaterialWithChaptersInDb(
        string title,
        Guid authorId,
        IReadOnlyList<string> chapterTitles,
        IReadOnlyList<int> chapterTimestamps,
        CancellationToken ct)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create(title).Value,
                MaterialKind.VIDEO,
                AccessType.PUBLIC);
            material.AttachVideo(VideoId.Create(Guid.CreateVersion7()).Value);
            material.UpdateChapters(chapterTitles, chapterTimestamps);
            Assert.True(material.Publish().IsSuccess);

            db.Materials.Add(material);

            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });

        return materialId;
    }

    private static bool RequiresCourseBinding(AccessType accessType)
        => accessType is AccessType.ENROLLED;
}
