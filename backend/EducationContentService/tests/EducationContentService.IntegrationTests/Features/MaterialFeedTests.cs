using ContentAccess;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class MaterialFeedTests : EducationContentServiceTestsBase
{
    public MaterialFeedTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ---------- Author feed ----------

    [Fact]
    public async Task AuthorFeed_Anonymous_ReturnsAllMaterialsWithLocks()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid publicId = await CreateMaterialAsync("Пост PUBLIC", authorId, AccessType.PUBLIC, ct);
        Guid enrolledId = await CreateMaterialAsync("Пост ENROLLED", authorId, AccessType.ENROLLED, ct);
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, publicId, AccessDecision.Granted(AccessReason.PUBLIC));
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, enrolledId, AccessDecision.Denied(AccessReason.NOT_AUTHENTICATED));

        RemoveAuthentication();

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        Assert.Equal(2, page.Items.Count);
        MaterialFeedItemDto pub = page.Items.First(i => i.Id == publicId);
        MaterialFeedItemDto enr = page.Items.First(i => i.Id == enrolledId);
        Assert.True(pub.IsAccessible);
        Assert.Null(pub.LockReason);
        Assert.NotNull(pub.Preview);
        Assert.False(enr.IsAccessible);
        Assert.Equal(MaterialLockReasons.Anonymous, enr.LockReason);
        Assert.Null(enr.Preview);
    }

    [Fact]
    public async Task AuthorFeed_AuthenticatedNoEnrollment_EnrolledLocked()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid enrolledId = await CreateMaterialAsync("ENROLLED пост", authorId, AccessType.ENROLLED, ct);

        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, enrolledId, AccessDecision.Denied());

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        MaterialFeedItemDto enrolled = Assert.Single(page.Items);
        Assert.Equal(enrolledId, enrolled.Id);
        Assert.Equal(MaterialLockReasons.NotEnrolled, enrolled.LockReason);
        Assert.Null(enrolled.Preview);
    }

    [Fact]
    public async Task AuthorFeed_Admin_AllAccessible()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreateMaterialAsync("Закрытый", authorId, AccessType.ENROLLED, ct);

        AuthenticateAsAdmin();

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        Assert.Single(page.Items);
        Assert.True(page.Items[0].IsAccessible);
    }

    [Fact]
    public async Task AuthorFeed_KindFilter_FiltersByKind()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreateMaterialAsync("Статья", authorId, AccessType.PUBLIC, ct, kind: MaterialKind.ARTICLE);
        Guid videoId = await CreateMaterialAsync("Видос", authorId, AccessType.PUBLIC, ct, kind: MaterialKind.VIDEO);

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10&kind={MaterialKind.VIDEO}", ct);

        Assert.Single(page.Items);
        Assert.Equal(videoId, page.Items[0].Id);
    }

    [Fact]
    public async Task AuthorFeed_DraftsExcluded()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        await CreateDraftAsync("Черновик", authorId, ct);
        await CreateMaterialAsync("Публикация", authorId, AccessType.PUBLIC, ct);

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        Assert.Single(page.Items);
        Assert.Equal("Публикация", page.Items[0].Title);
    }

    // ---------- Course feed ----------

    [Fact]
    public async Task CourseFeed_ReturnsMaterialsFromModulesAndDirectAttachments()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid courseId = await CreateCourseAsync(authorId, ct);
        Guid moduleId = await CreateModuleAsync(authorId, ct);
        await AttachModuleToCourseAsync(courseId, moduleId, ct);

        Guid moduleMaterialId = await CreateMaterialAsync("Из модуля", authorId, AccessType.PUBLIC, ct);
        Guid directMaterialId = await CreateMaterialAsync("Напрямую", authorId, AccessType.PUBLIC, ct);

        await AttachMaterialToModuleAsync(moduleId, moduleMaterialId, ct);
        await AttachMaterialToCourseAsync(courseId, directMaterialId, ct);

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/courses/{courseId}/materials/feed?limit=10", ct);

        Assert.Equal(2, page.Items.Count);
        Assert.Contains(page.Items, i => i.Id == moduleMaterialId);
        Assert.Contains(page.Items, i => i.Id == directMaterialId);
    }

    [Fact]
    public async Task CourseFeed_OrderByPublishedDesc()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid courseId = await CreateCourseAsync(authorId, ct);

        Guid firstId = await CreateMaterialAsync("Старый", authorId, AccessType.PUBLIC, ct);
        await Task.Delay(10, ct);
        Guid secondId = await CreateMaterialAsync("Новый", authorId, AccessType.PUBLIC, ct);

        await AttachMaterialToCourseAsync(courseId, firstId, ct);
        await AttachMaterialToCourseAsync(courseId, secondId, ct);

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/courses/{courseId}/materials/feed?limit=10", ct);

        Assert.Equal(2, page.Items.Count);
        Assert.Equal(secondId, page.Items[0].Id);
        Assert.Equal(firstId, page.Items[1].Id);
    }

    [Fact]
    public async Task CourseMaterialTags_ReturnsOnlyTagsUsedByPublishedMaterialsInCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid courseId = await CreateCourseAsync(authorId, ct);
        Guid otherCourseId = await CreateCourseAsync(authorId, ct);

        Guid courseMaterialId = await CreateMaterialAsync("Kubernetes basics", authorId, AccessType.PUBLIC, ct);
        Guid otherCourseMaterialId = await CreateMaterialAsync("React basics", authorId, AccessType.PUBLIC, ct);
        Guid draftMaterialId = await CreateDraftAsync("Draft with tag", authorId, ct);

        await AttachMaterialToCourseAsync(courseId, courseMaterialId, ct);
        await AttachMaterialToCourseAsync(courseId, draftMaterialId, ct);
        await AttachMaterialToCourseAsync(otherCourseId, otherCourseMaterialId, ct);

        Guid k8sTagId = await CreateTagAsync("Kubernetes", ct);
        Guid reactTagId = await CreateTagAsync("React", ct);
        Guid draftTagId = await CreateTagAsync("Draft", ct);

        await LinkTagAsync(k8sTagId, courseMaterialId, ct);
        await LinkTagAsync(reactTagId, otherCourseMaterialId, ct);
        await LinkTagAsync(draftTagId, draftMaterialId, ct);

        RemoveAuthentication();

        IReadOnlyList<TagListItem> tags = await GetCourseMaterialTagsAsync(courseId, ct);

        TagListItem tag = Assert.Single(tags);
        Assert.Equal(k8sTagId, tag.Id);
        Assert.Equal("Kubernetes", tag.Title);
        Assert.Equal("kubernetes", tag.Slug);
        Assert.Equal("canon", tag.Kind);
    }

    // ---------- Thumbnail priority ----------

    [Fact]
    public async Task AuthorFeed_VideoMaterialWithBothImageAndVideo_PrefersManualCoverOverVideoThumb()
    {
        // Симптом: автор VIDEO-материала загрузил кастомную обложку (ImageId),
        // но в ленте показывается автогенерируемый Kinescope thumbnail (VideoId).
        // Контракт: явная обложка автора имеет приоритет; video-thumb — fallback
        // только когда ImageId пуст.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid imageAssetId = Guid.CreateVersion7();
        Guid videoAssetId = Guid.CreateVersion7();
        const string ManualCoverUrl = "https://files.example/manual-cover.png";
        const string VideoThumbUrl = "https://files.example/video-thumb.png";

        Guid materialId = await CreateMaterialWithMediaAsync(
            "Видео с кастомной обложкой", authorId, AccessType.PUBLIC,
            imageId: imageAssetId, videoId: videoAssetId, ct: ct);
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, materialId, AccessDecision.Granted(AccessReason.PUBLIC));

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.GetFilesBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<GetFileResponse>?, Error>(
                [new GetFileResponse(imageAssetId, "FILE", "MATERIAL_PREVIEW", "READY",
                    "cover.png", "image/png", 1024, ManualCoverUrl, null, false)]));
        fileClient.GetVideosBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<GetPublicVideoResponse>?, Error>(
                [new GetPublicVideoResponse(videoAssetId, VideoThumbUrl, 60.0)]));

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        MaterialFeedItemDto item = Assert.Single(page.Items);
        Assert.Equal(materialId, item.Id);
        Assert.Equal(ManualCoverUrl, item.ThumbnailUrl);
    }

    [Fact]
    public async Task AuthorFeed_VideoMaterialWithoutImage_FallsBackToVideoThumb()
    {
        // Negative-case: только VideoId без ImageId — video-thumb используется как fallback.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid videoAssetId = Guid.CreateVersion7();
        const string VideoThumbUrl = "https://files.example/video-thumb-only.png";

        Guid materialId = await CreateMaterialWithMediaAsync(
            "Видео без обложки", authorId, AccessType.PUBLIC,
            imageId: null, videoId: videoAssetId, ct: ct);
        EntitlementChecker.SetDecision(ResourceTypes.MATERIAL, materialId, AccessDecision.Granted(AccessReason.PUBLIC));

        var fileClient = Services.GetRequiredService<IFileServiceClient>();
        fileClient.GetVideosBatchAsync(Arg.Any<IReadOnlyList<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<List<GetPublicVideoResponse>?, Error>(
                [new GetPublicVideoResponse(videoAssetId, VideoThumbUrl, 60.0)]));

        AuthenticateAs(Guid.CreateVersion7(), "platform-participant");

        CursorResponse<MaterialFeedItemDto> page = await GetFeedAsync(
            $"/authors/{authorId}/materials/feed?limit=10", ct);

        MaterialFeedItemDto item = Assert.Single(page.Items);
        Assert.Equal(VideoThumbUrl, item.ThumbnailUrl);
        // Длительность приезжает из того же video-batch'а (#500).
        Assert.Equal(60.0, item.DurationSeconds);
    }

    // ---------- Helpers ----------

    private async Task<CursorResponse<MaterialFeedItemDto>> GetFeedAsync(string url, CancellationToken ct)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await ReadResultAsync<CursorResponse<MaterialFeedItemDto>>(response);
    }

    private async Task<IReadOnlyList<TagListItem>> GetCourseMaterialTagsAsync(Guid courseId, CancellationToken ct)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/materials/tags", ct);
        response.EnsureSuccessStatusCode();
        return await ReadResultAsync<IReadOnlyList<TagListItem>>(response);
    }

    private async Task<Guid> CreateMaterialAsync(
        string title,
        Guid authorId,
        AccessType accessType,
        CancellationToken ct,
        MaterialKind kind = MaterialKind.ARTICLE)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            int boundCourseCount = RequiresCourseBinding(accessType) ? 1 : 0;
            var material = new Material(authorId, Title.Create(title).Value, kind, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                kind,
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
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateMaterialWithMediaAsync(
        string title,
        Guid authorId,
        AccessType accessType,
        Guid? imageId,
        Guid? videoId,
        CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create(title).Value, MaterialKind.VIDEO, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create($"# {title}\n\nТело").Value,
                MaterialKind.VIDEO,
                accessType,
                boundCourseCount: RequiresCourseBinding(accessType) ? 1 : 0,
                description: null);
            if (imageId is { } iid)
                material.AttachImage(ImageId.Create(iid).Value);
            if (videoId is { } vid)
                material.AttachVideo(VideoId.Create(vid).Value);
            Assert.True(material.Publish().IsSuccess);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private async Task<Guid> CreateDraftAsync(string title, Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create(title).Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create("# draft").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC,
                boundCourseCount: 0,
                description: null);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            id = material.Id;
        });
        return id;
    }

    private static bool RequiresCourseBinding(AccessType accessType)
        => accessType is AccessType.ENROLLED;

    private async Task<Guid> CreateCourseAsync(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            string suffix = Guid.CreateVersion7().ToString("N");
            var course = new Course(
                authorId,
                Title.Create($"Курс {suffix}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create($"course-{suffix}").Value, SortKey.Initial());
            Assert.True(course.Publish().IsSuccess);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task<Guid> CreateModuleAsync(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create("Модуль 1").Value,
                Description.Create("Описание").Value);
            Assert.True(module.Publish().IsSuccess);
            db.Modules.Add(module);
            await db.SaveChangesAsync(ct);
            id = module.Id;
        });
        return id;
    }

    private async Task AttachModuleToCourseAsync(Guid courseId, Guid moduleId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            var item = new CourseItem(
                courseId,
                CourseItemType.Module,
                moduleId,
                SortKey.Initial(),
                isOptional: false);
            db.CourseItems.Add(item);
            await db.SaveChangesAsync(ct);
        });
    }

    private async Task AttachMaterialToModuleAsync(Guid moduleId, Guid materialId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            var item = new ModuleItem(
                moduleId,
                ModuleItemType.Material,
                materialId,
                SortKey.Initial(),
                isOptional: false);
            db.ModuleItems.Add(item);

            // MATERIAL_LIFECYCLE.md INV-4: материал в module_items курса ⟹ есть course_materials.
            // Helper создаёт обе записи, чтобы отражать инвариант, который auto-создаёт
            // use-case AttachMaterialToModule.
            Guid? courseId = await db.CourseItems
                .Where(ci => ci.ReferenceId == moduleId && ci.ItemType == CourseItemType.Module)
                .Select(ci => (Guid?)ci.CourseId)
                .FirstOrDefaultAsync(ct);

            if (courseId is { } cid)
            {
                bool hasCourseMaterial = await db.CourseMaterials
                    .AnyAsync(cm => cm.CourseId == cid && cm.MaterialId == materialId, ct);
                if (!hasCourseMaterial)
                {
                    db.CourseMaterials.Add(new CourseMaterial(cid, materialId, SortKey.Initial()));
                }
            }

            await db.SaveChangesAsync(ct);
        });
    }

    private async Task AttachMaterialToCourseAsync(Guid courseId, Guid materialId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            var cm = new CourseMaterial(
                courseId,
                materialId,
                SortKey.Initial());
            db.CourseMaterials.Add(cm);
            await db.SaveChangesAsync(ct);
        });
    }

    private async Task<Guid> CreateTagAsync(string title, CancellationToken ct)
    {
        Guid id = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tags.tags (id, title, slug, kind)
                VALUES ({id}, {title}, {title.ToLowerInvariant()}, 'CANON');
                """, ct);
        });
        return id;
    }

    private async Task LinkTagAsync(Guid tagId, Guid materialId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tags.entity_tags (id, entity_type, entity_id, tag_id)
                VALUES ({Guid.CreateVersion7()}, 'Material', {materialId}, {tagId});
                """, ct);
        });
    }

    private sealed record TagListItem(Guid Id, string Title, string Slug, string Kind);
}
