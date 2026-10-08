using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class GetCurriculumTests : EducationContentServiceTestsBase
{
    public GetCurriculumTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetCurriculum_Anonymous_PublishedCourse_ShouldReturnOk()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Published Curriculum", "Desc", publish: true, ct);

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/curriculum", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseCurriculumDto dto = await ReadResultAsync<CourseCurriculumDto>(response);
        Assert.Equal(courseId, dto.Id);
        Assert.NotEqual(Guid.Empty, dto.AuthorId);
        Assert.Equal("Published Curriculum", dto.Title);
        Assert.Equal("PUBLISHED", dto.Status);
    }

    [Fact]
    public async Task GetCurriculum_Admin_DraftCourse_ShouldReturn404()
    {
        // Curriculum endpoint is strictly PUBLISHED-only (same as landing). DRAFT courses
        // return 404 regardless of caller — even an Admin with FakeEntitlementChecker.GrantAll
        // can't surface unpublished curriculum; authors use /courses/{id}/detail for the
        // full draft tree instead.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Draft Curriculum", "Desc", publish: false, ct);

        EntitlementChecker.GrantAll();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/curriculum", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCurriculum_ProjectWithDetailedDescription_ReturnsDetailedDescriptionInSection()
    {
        // Arrange — published course; create+publish a project carrying a markdown brief.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Course With Brief", "Desc", publish: true, ct);

        EntitlementChecker.GrantAll();
        AuthenticateAsAdmin();

        const string detailed = "## Что предстоит сделать\n\nПостроить gRPC-сервис.";
        var createReq = new CreateCourseProjectRequest("Directory Service", "Краткая аннотация", detailed);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/projects", createReq, ct);
        createResp.EnsureSuccessStatusCode();
        Envelope<Guid>? createEnv = await createResp.Content.ReadFromJsonAsync<Envelope<Guid>>(ct);
        Guid projectId = createEnv!.Result;

        HttpResponseMessage publishResp = await AppHttpClient.PostAsync(
            $"/projects/{projectId}/publish", content: null, ct);
        publishResp.EnsureSuccessStatusCode();

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/curriculum", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseCurriculumDto dto = await ReadResultAsync<CourseCurriculumDto>(response);
        CurriculumSectionDto? projectSection = dto.Sections.FirstOrDefault(s => s.ItemType == "Project");
        Assert.NotNull(projectSection);
        Assert.Equal("Directory Service", projectSection.Title);
        Assert.Equal("Краткая аннотация", projectSection.Description);
        Assert.Equal(detailed, projectSection.DetailedDescription);
    }

    [Fact]
    public async Task GetCourseBuilder_ProjectWithDetailedDescription_ReturnsItInSection()
    {
        // Regression (#340): the builder feeds the author Edit-Project dialog. It must
        // COALESCE the project's detailed_description (not just the module's) — otherwise
        // the dialog pre-loads null and saving wipes the stored brief.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Builder Course", "Desc", publish: false, ct);

        EntitlementChecker.GrantAll();
        AuthenticateAsAdmin();

        const string detailed = "## Бриф\n\nСделать X.";
        var createReq = new CreateCourseProjectRequest("Builder Project", "Аннотация", detailed);
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/projects", createReq, ct);
        createResp.EnsureSuccessStatusCode();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/builder", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseBuilderDto dto = await ReadResultAsync<CourseBuilderDto>(response);
        BuilderSectionDto? projectSection = dto.Sections.FirstOrDefault(s => s.ItemType == "Project");
        Assert.NotNull(projectSection);
        Assert.Equal("Аннотация", projectSection.Description);
        Assert.Equal(detailed, projectSection.DetailedDescription);
    }

    [Fact]
    public async Task GetCurriculum_CourseWithCollections_ReturnsPublishedCollectionsOnly()
    {
        // #508: программа курса отдаёт PUBLISHED-подборки отдельным списком.
        // ItemsCount/MaterialIds считают только PUBLISHED-материалы (DISTINCT —
        // материал в двух секциях не задваивается), зеркаля blueprint #496.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Course With Collections", "Desc", publish: true, ct);
        Guid otherCourseId = await CreateCourseInDbAsync("Other Course", "Desc", publish: true, ct);

        Guid publishedCollectionId = Guid.Empty;
        Guid publishedMaterial1 = Guid.Empty;
        Guid publishedMaterial2 = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            Guid authorId = Guid.CreateVersion7();

            var m1 = new Material(authorId, Title.Create("Опубликованный 1").Value);
            var m2 = new Material(authorId, Title.Create("Опубликованный 2").Value);
            var draft = new Material(authorId, Title.Create("Черновик").Value);
            m1.Update(m1.Title, MarkdownContent.Create("# 1").Value, MaterialKind.ARTICLE, AccessType.PUBLIC, 0, null);
            m2.Update(m2.Title, MarkdownContent.Create("# 2").Value, MaterialKind.ARTICLE, AccessType.PUBLIC, 0, null);
            Assert.True(m1.Publish().IsSuccess);
            Assert.True(m2.Publish().IsSuccess);
            db.Materials.AddRange(m1, m2, draft);
            publishedMaterial1 = m1.Id;
            publishedMaterial2 = m2.Id;

            // PUBLISHED course-bound подборка: 2 published материала (один из них
            // продублирован во второй секции) + 1 draft.
            var published = new Collection(authorId, Title.Create("Полезные ссылки").Value, courseId);
            var section1 = new CollectionSection(published.Id, "Секция 1", null, SortKey.Initial());
            var section2 = new CollectionSection(published.Id, "Секция 2", null, SortKey.Initial());
            db.Collections.Add(published);
            db.CollectionSections.AddRange(section1, section2);
            db.CollectionItems.AddRange(
                new CollectionItem(section1.Id, CollectionItemType.MATERIAL, m1.Id, SortKey.Initial()),
                new CollectionItem(section1.Id, CollectionItemType.MATERIAL, draft.Id, SortKey.Initial()),
                new CollectionItem(section2.Id, CollectionItemType.MATERIAL, m2.Id, SortKey.Initial()),
                new CollectionItem(section2.Id, CollectionItemType.MATERIAL, m1.Id, SortKey.Initial()));
            Assert.True(published.Publish(hasAnyItem: true).IsSuccess);
            publishedCollectionId = published.Id;

            // DRAFT подборка того же курса — не должна попасть в выдачу.
            var draftCollection = new Collection(authorId, Title.Create("Черновая подборка").Value, courseId);
            var draftSection = new CollectionSection(draftCollection.Id, null, null, SortKey.Initial());
            db.Collections.Add(draftCollection);
            db.CollectionSections.Add(draftSection);
            db.CollectionItems.Add(new CollectionItem(draftSection.Id, CollectionItemType.MATERIAL, m1.Id, SortKey.Initial()));

            // PUBLISHED подборка ДРУГОГО курса — не должна попасть в выдачу.
            var foreign = new Collection(authorId, Title.Create("Чужая подборка").Value, otherCourseId);
            var foreignSection = new CollectionSection(foreign.Id, null, null, SortKey.Initial());
            db.Collections.Add(foreign);
            db.CollectionSections.Add(foreignSection);
            db.CollectionItems.Add(new CollectionItem(foreignSection.Id, CollectionItemType.MATERIAL, m2.Id, SortKey.Initial()));
            Assert.True(foreign.Publish(hasAnyItem: true).IsSuccess);

            await db.SaveChangesAsync(ct);
        });

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/curriculum", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseCurriculumDto dto = await ReadResultAsync<CourseCurriculumDto>(response);

        CurriculumCollectionDto collection = Assert.Single(dto.Collections);
        Assert.Equal(publishedCollectionId, collection.Id);
        Assert.Equal("Полезные ссылки", collection.Title);
        Assert.Equal("ENROLLED", collection.AccessType);
        Assert.Equal(2, collection.ItemsCount);
        Assert.Equal(
            new[] { publishedMaterial1, publishedMaterial2 }.OrderBy(id => id),
            collection.MaterialIds.OrderBy(id => id));
    }

    [Fact]
    public async Task GetCurriculum_NonExistentCourse_ShouldReturn404()
    {
        CancellationToken ct = CancellationToken.None;

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{Guid.NewGuid()}/curriculum", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseInDbAsync(
        string title, string description, bool publish, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            string slug = $"curriculum-{Guid.NewGuid().ToString("N")[..8]}";
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create(description).Value,
                CourseSlug.Create(slug).Value, SortKey.Initial());

            if (publish)
                course.Publish();

            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }
}
