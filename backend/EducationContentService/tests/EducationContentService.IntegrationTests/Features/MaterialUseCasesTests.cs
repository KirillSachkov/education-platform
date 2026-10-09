using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Projects.ValueObjects;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class MaterialUseCasesTests : EducationContentServiceTestsBase
{
    public MaterialUseCasesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateMaterial_AsAuthor_ReturnsSuccess_AndMaterialIsInDraftStatus()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        var request = new CreateMaterialRequest(
            "Первый материал",
            "# Содержимое",
            MaterialKind.ARTICLE.ToString(),
            AccessType.PUBLIC.ToString());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials.FirstOrDefaultAsync(ct);
            Assert.NotNull(material);
            Assert.Equal(authorId, material.AuthorId);
            Assert.Equal("Первый материал", material.Title.Value);
            Assert.NotNull(material.Content);
            Assert.Equal("# Содержимое", material.Content!.Value);
            Assert.Equal(PublicationStatus.DRAFT, material.Status);
            Assert.Equal(MaterialKind.ARTICLE, material.Kind);
            Assert.Equal(AccessType.PUBLIC, material.AccessType);
        });
    }

    [Fact]
    public async Task CreateMaterial_OrphanEnrolled_Succeeds_AfterPlanBoundRefactor()
    {
        // После #77: orphan материал с AccessType=ENROLLED легитимен — гейт через платформенный plan:all.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        var request = new CreateMaterialRequest(
            "Материал без курса",
            "# Содержимое",
            MaterialKind.ARTICLE.ToString(),
            AccessType.ENROLLED.ToString());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials
                .FirstOrDefaultAsync(m => m.AuthorId == authorId, ct);
            Assert.NotNull(material);
            Assert.Equal(AccessType.ENROLLED, material.AccessType);
        });
    }

    [Fact]
    public async Task CreateMaterial_AsAdmin_WithAuthorIdOverride_UsesOverride()
    {
        // Admin-only override используется MCP client_credentials caller'ом: его UserId=Guid.Empty,
        // и без явного override материал ушёл бы под пустого автора → сорванные Redis-теги.
        CancellationToken ct = CancellationToken.None;
        Guid serviceCallerId = Guid.Empty;
        Guid targetAuthorId = Guid.CreateVersion7();
        AuthenticateAs(serviceCallerId, "platform-admin");

        var request = new CreateMaterialRequest(
            Title: "Материал от имени автора",
            Content: "# Текст",
            Kind: MaterialKind.ARTICLE.ToString(),
            AccessType: AccessType.ENROLLED.ToString(),
            AuthorId: targetAuthorId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? material = await db.Materials
                .FirstOrDefaultAsync(m => m.AuthorId == targetAuthorId, ct);
            Assert.NotNull(material);
            Assert.Equal(targetAuthorId, material.AuthorId);
        });
    }

    [Fact]
    public async Task CreateMaterial_AsAuthor_AuthorIdInPayload_IsIgnored()
    {
        // Non-admin caller передал AuthorId — должен быть проигнорирован, author = caller.
        CancellationToken ct = CancellationToken.None;
        Guid callerId = Guid.CreateVersion7();
        Guid spoofedAuthorId = Guid.CreateVersion7();
        AuthenticateAs(callerId, "platform-author");

        var request = new CreateMaterialRequest(
            Title: "Попытка spoof'а",
            Content: "# Текст",
            Kind: MaterialKind.ARTICLE.ToString(),
            AccessType: AccessType.PUBLIC.ToString(),
            AuthorId: spoofedAuthorId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material? callerMaterial = await db.Materials
                .FirstOrDefaultAsync(m => m.AuthorId == callerId, ct);
            Material? spoofedMaterial = await db.Materials
                .FirstOrDefaultAsync(m => m.AuthorId == spoofedAuthorId, ct);
            Assert.NotNull(callerMaterial);
            Assert.Null(spoofedMaterial);
        });
    }

    [Fact]
    public async Task CreateMaterial_DuplicateTitle_ReturnsError()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        // Создаём сперва опубликованный материал (ExistsByTitleAsync ищет только Published/Archived).
        await CreatePublishedMaterialInDb("Дубликат", "# Body", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        var request = new CreateMaterialRequest(
            "Дубликат",
            "# New",
            MaterialKind.ARTICLE.ToString(),
            AccessType.ENROLLED.ToString());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task UpdateMaterial_AsOwner_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Старый заголовок", "# Старое", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        var request = new UpdateMaterialRequest(
            "Новый заголовок",
            "# Новое содержимое",
            MaterialKind.VIDEO.ToString(),
            AccessType.REGISTERED.ToString());

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/materials/{materialId}", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal("Новый заголовок", material.Title.Value);
            Assert.Equal("# Новое содержимое", material.Content!.Value);
            Assert.Equal(MaterialKind.VIDEO, material.Kind);
            Assert.Equal(AccessType.REGISTERED, material.AccessType);
        });
    }

    [Fact]
    public async Task UpdateMaterial_AsNonOwner_ReturnsUnauthorized()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Чужой", "# Body", ownerId, ct: ct);

        Guid otherUserId = Guid.CreateVersion7();
        AuthenticateAs(otherUserId, "platform-author");

        var request = new UpdateMaterialRequest(
            "Перехват",
            "# Hack",
            MaterialKind.ARTICLE.ToString(),
            AccessType.ENROLLED.ToString());

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/materials/{materialId}", request, ct);

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task PublishMaterial_WithContent_Succeeds_AndStatusIsPublished()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("К публикации", "# Content", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/publish", content: null, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
        });
    }

    [Fact]
    public async Task PublishMaterial_AsModerator_OnAnotherAuthorsMaterial_BypassesOwnership_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Модерируемый чужой", "# Content", ownerId, ct: ct);

        // Moderator is NOT the author — Content.MODERATE bypasses the Tier-2 ownership guard.
        Guid moderatorId = Guid.CreateVersion7();
        AuthenticateAs(moderatorId, "platform-moderator");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/publish", content: null, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(PublicationStatus.PUBLISHED, material.Status);
        });
    }

    [Fact]
    public async Task PublishMaterial_AsDifferentAuthor_WithoutModeratePermission_ReturnsForbidden()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Чужой автор", "# Content", ownerId, ct: ct);

        // A plain author who does not own the material and lacks Content.MODERATE is blocked.
        Guid otherAuthorId = Guid.CreateVersion7();
        AuthenticateAs(otherAuthorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/publish", content: null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(PublicationStatus.DRAFT, material.Status);
        });
    }

    [Fact]
    public async Task PublishMaterial_WithoutContentOrVideo_ReturnsError()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Пустышка", content: null, authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/publish", content: null, ct);

        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task SendToDraftMaterial_FromPublished_ReturnsSuccess()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb("Back to draft", "# Body", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/draft", content: null, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(PublicationStatus.DRAFT, material.Status);
        });
    }

    [Fact]
    public async Task ArchiveMaterial_FromPublished_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreatePublishedMaterialInDb("To archive", "# Body", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/materials/{materialId}/archive", content: null, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(PublicationStatus.ARCHIVED, material.Status);
        });
    }

    [Fact]
    public async Task DeleteMaterial_AsOwner_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("To delete", "# Body", authorId, ct: ct);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/materials/{materialId}", ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            bool exists = await db.Materials.AnyAsync(m => m.Id == materialId, ct);
            Assert.False(exists);
        });
    }

    [Fact]
    public async Task DeleteMaterial_WithAllReferenceTypes_CascadesAndSucceeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = Guid.Empty;
        Guid otherMaterialId = Guid.Empty;
        Guid courseId = Guid.Empty;
        Guid moduleId = Guid.Empty;
        Guid sectionId = Guid.Empty;
        Guid issueId = Guid.Empty;
        Guid quizId = Guid.Empty;
        Guid otherQuizId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var material = new Material(authorId, Title.Create("Linked").Value);
            material.SetContent(MarkdownContent.Create("body").Value);
            db.Materials.Add(material);
            materialId = material.Id;

            // Второй материал — следим, что cascade его не задел.
            var otherMaterial = new Material(authorId, Title.Create("Other").Value);
            otherMaterial.SetContent(MarkdownContent.Create("body").Value);
            db.Materials.Add(otherMaterial);
            otherMaterialId = otherMaterial.Id;

            var course = new Course(
                authorId,
                Title.Create("Хост-курс").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value, SortKey.Initial());
            db.Courses.Add(course);
            courseId = course.Id;
            db.CourseMaterials.Add(new CourseMaterial(courseId, materialId, SortKey.Initial()));

            var module = new Module(authorId, Title.Create("Модуль").Value);
            db.Set<Module>().Add(module);
            moduleId = module.Id;
            db.Set<ModuleItem>().Add(new ModuleItem(
                moduleId, ModuleItemType.Material, materialId, SortKey.Initial(), isOptional: false));

            var collection = new Collection(authorId, Title.Create("Подборка").Value);
            db.Set<Collection>().Add(collection);
            var section = new CollectionSection(collection.Id, "Раздел", null, SortKey.Initial());
            db.Set<CollectionSection>().Add(section);
            sectionId = section.Id;
            db.Set<CollectionItem>().Add(new CollectionItem(sectionId, CollectionItemType.MATERIAL, materialId, SortKey.Initial()));

            // Квизы standalone (#489): материал ссылается на квиз через materials.quiz_id.
            // Hard-delete материала сносит его строку (и ссылку вместе с ней), но сам
            // квиз ЖИВЁТ — он самостоятельная сущность.
            Quiz quiz = Quiz.Create(
                authorId, Title.Create("Квиз материала").Value, []).Value;
            db.Quizzes.Add(quiz);
            quizId = quiz.Id;
            material.AttachQuiz(quiz.Id);

            Quiz otherQuiz = Quiz.Create(
                authorId, Title.Create("Квиз другого материала").Value, []).Value;
            db.Quizzes.Add(otherQuiz);
            otherQuizId = otherQuiz.Id;
            otherMaterial.AttachQuiz(otherQuiz.Id);

            // Issue с двумя internal materials — целевой и оставшийся, проверяем фильтрацию JSONB.
            var project = new Project(authorId, Title.Create("Проект").Value);
            db.Set<Project>().Add(project);
            var issue = new Issue(
                authorId,
                project.Id,
                Title.Create("Задача").Value,
                MarkdownContent.Create("body").Value);
            issue.UpdateInternalMaterials(new List<IssueInternalMaterial>
            {
                IssueInternalMaterial.Create(ModuleItemType.Material, materialId, isRequired: true).Value,
                IssueInternalMaterial.Create(ModuleItemType.Material, otherMaterialId, isRequired: false).Value,
            });
            db.Set<Issue>().Add(issue);
            issueId = issue.Id;

            await db.SaveChangesAsync(ct);
        });

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/materials/{materialId}", ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.Materials.AnyAsync(m => m.Id == materialId, ct));
            Assert.True(await db.Materials.AnyAsync(m => m.Id == otherMaterialId, ct));

            Assert.False(await db.CourseMaterials.AnyAsync(cm => cm.MaterialId == materialId, ct));
            Assert.False(await db.Set<ModuleItem>()
                .AnyAsync(mi => mi.ReferenceId == materialId && mi.ItemType == ModuleItemType.Material, ct));
            Assert.False(await db.Set<CollectionItem>()
                .AnyAsync(ci => ci.ReferenceId == materialId && ci.ItemType == CollectionItemType.MATERIAL, ct));

            // Квизы — standalone (#489): материал удалён (строка с quiz_id исчезла),
            // но ОБА квиза живут; ссылка другого материала не задета.
            Assert.True(await db.Quizzes.AnyAsync(q => q.Id == quizId, ct));
            Assert.True(await db.Quizzes.AnyAsync(q => q.Id == otherQuizId, ct));
            Assert.Equal(otherQuizId, (await db.Materials.AsNoTracking()
                .FirstAsync(m => m.Id == otherMaterialId, ct)).QuizId);

            // Issue: материал-цель отстрелен, второй internal-material остался.
            Issue issue = await db.Set<Issue>().AsNoTracking().FirstAsync(i => i.Id == issueId, ct);
            Assert.DoesNotContain(issue.InternalMaterials, m => m.ReferenceId == materialId);
            Assert.Contains(issue.InternalMaterials, m => m.ReferenceId == otherMaterialId);

        });
    }

    [Fact]
    public async Task DeleteMaterial_AsNonOwner_ReturnsUnauthorized()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.CreateVersion7();
        Guid materialId = await CreateDraftMaterialInDb("Not yours", "# Body", ownerId, ct: ct);

        Guid otherUserId = Guid.CreateVersion7();
        AuthenticateAs(otherUserId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/materials/{materialId}", ct);

        Assert.False(response.IsSuccessStatusCode);
    }

    private async Task<Guid> CreateDraftMaterialInDb(
        string title,
        string? content,
        Guid authorId,
        MaterialKind kind = MaterialKind.ARTICLE,
        AccessType accessType = AccessType.ENROLLED,
        CancellationToken ct = default)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            int boundCourseCount = RequiresCourseBinding(accessType) ? 1 : 0;
            var material = new Material(authorId, Title.Create(title).Value, kind, accessType);
            MarkdownContent? markdownContent = !string.IsNullOrWhiteSpace(content)
                ? MarkdownContent.Create(content).Value
                : null;
            Assert.True(material.Update(
                Title.Create(title).Value,
                markdownContent,
                kind,
                accessType,
                boundCourseCount,
                description: null).IsSuccess);

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

    private async Task<Guid> CreatePublishedMaterialInDb(
        string title,
        string content,
        Guid authorId,
        MaterialKind kind = MaterialKind.ARTICLE,
        AccessType accessType = AccessType.ENROLLED,
        CancellationToken ct = default)
    {
        Guid materialId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            int boundCourseCount = RequiresCourseBinding(accessType) ? 1 : 0;
            var material = new Material(authorId, Title.Create(title).Value, kind, accessType);
            material.Update(
                Title.Create(title).Value,
                MarkdownContent.Create(content).Value,
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
            materialId = material.Id;
        });

        return materialId;
    }

    private static bool RequiresCourseBinding(AccessType accessType)
        => accessType is AccessType.ENROLLED;

    [Fact]
    public async Task UpdateVideoChapters_FanOutsToAllMaterialsWithVideoIdAndUpdatesDb()
    {
        // L2 на session.Sent тут невалиден: ECS test factory подменяет IOutboxService
        // на TestOutboxService (no-op), Wolverine TrackActivity их не трекает.
        // Проверяем DB-state напрямую — handler нашёл оба материала по videoId,
        // обновил chapter_titles + chapter_timestamps, чужой видео не задел.
        CancellationToken ct = CancellationToken.None;
        Guid serviceId = Guid.CreateVersion7();
        AuthenticateAs(serviceId, "platform-service");

        Guid videoId = Guid.CreateVersion7();
        Guid otherVideoId = Guid.CreateVersion7();
        Guid authorId = Guid.CreateVersion7();

        Guid materialAId = await CreateMaterialWithVideoInDb("Видео A", videoId, authorId, ct);
        Guid materialBId = await CreateMaterialWithVideoInDb("Видео B", videoId, authorId, ct);
        Guid materialOtherId = await CreateMaterialWithVideoInDb("Чужой", otherVideoId, authorId, ct);

        var request = new UpdateVideoChaptersRequest(
            AssetVersion: Guid.CreateVersion7(),
            Chapters:
            [
                new VideoChapterDto("Введение", 0),
                new VideoChapterDto("Архитектура", 120),
                new VideoChapterDto("Q&A", 1800),
            ]);

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/internal/videos/{videoId}/chapters/", request, ct);
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Material a = await db.Materials.SingleAsync(m => m.Id == materialAId, ct);
            Assert.Equal(["Введение", "Архитектура", "Q&A"], a.ChapterTitles);
            Assert.Equal([0, 120, 1800], a.ChapterTimestamps);
            Material b = await db.Materials.SingleAsync(m => m.Id == materialBId, ct);
            Assert.Equal(["Введение", "Архитектура", "Q&A"], b.ChapterTitles);
            Assert.Equal([0, 120, 1800], b.ChapterTimestamps);
            Material other = await db.Materials.SingleAsync(m => m.Id == materialOtherId, ct);
            Assert.Empty(other.ChapterTitles);
            Assert.Empty(other.ChapterTimestamps);
        });
    }

    [Fact]
    public async Task UpdateVideoChapters_RepeatWithSameContent_NoOps()
    {
        // Идемпотентность: повторный вызов с теми же главами не должен бить
        // updated_at дважды. Проверяем через сравнение updated_at до и после
        // второго вызова — TestOutboxService no-op не позволяет проверить
        // event-publish, так что fallback на DB-state.
        CancellationToken ct = CancellationToken.None;
        Guid serviceId = Guid.CreateVersion7();
        AuthenticateAs(serviceId, "platform-service");

        Guid videoId = Guid.CreateVersion7();
        Guid authorId = Guid.CreateVersion7();
        Guid materialId = await CreateMaterialWithVideoInDb("Видео", videoId, authorId, ct);

        var request = new UpdateVideoChaptersRequest(
            AssetVersion: Guid.CreateVersion7(),
            Chapters: [new VideoChapterDto("Глава", 60)]);

        HttpResponseMessage first = await AppHttpClient.PutAsJsonAsync(
            $"/internal/videos/{videoId}/chapters/", request, ct);
        first.EnsureSuccessStatusCode();

        DateTime updatedAfterFirst = await ExecuteInDb(async db =>
            await db.Materials.Where(m => m.Id == materialId).Select(m => m.UpdatedAt).SingleAsync(ct));

        HttpResponseMessage second = await AppHttpClient.PutAsJsonAsync(
            $"/internal/videos/{videoId}/chapters/", request, ct);
        second.EnsureSuccessStatusCode();

        DateTime updatedAfterSecond = await ExecuteInDb(async db =>
            await db.Materials.Where(m => m.Id == materialId).Select(m => m.UpdatedAt).SingleAsync(ct));

        Assert.Equal(updatedAfterFirst, updatedAfterSecond);
    }

    private async Task<Guid> CreateMaterialWithVideoInDb(
        string title,
        Guid videoId,
        Guid authorId,
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
            material.AttachVideo(VideoId.Create(videoId).Value);
            db.Materials.Add(material);
            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }
}