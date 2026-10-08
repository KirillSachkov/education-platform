using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Collections;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class GetCourseProgressBlueprintsTests : EducationContentServiceTestsBase
{
    public GetCourseProgressBlueprintsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Blueprint_Includes_CourseAttachedMaterials()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid materialId = await CreatePublishedMaterialInDb(courseId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds);
        Assert.Equal(materialId, blueprint.MaterialIds[0]);
    }

    [Fact]
    public async Task Blueprint_Includes_MaterialsFromPublishedCollection()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid materialId = await CreatePublishedMaterialInDb(courseId: null, ct);

        AuthenticateAsAdmin();
        Guid collectionId = await CreateCourseLevelCollectionAsync(courseId, ct);
        await AddMaterialToCollectionAsync(collectionId, materialId, ct);
        await PublishCollectionAsync(collectionId, ct);

        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds, materialId);
    }

    [Fact]
    public async Task Blueprint_Excludes_MaterialsFromDraftCollection()
    {
        // collection.status='PUBLISHED' filter — DRAFT-подборка не должна влиять на total.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid materialId = await CreatePublishedMaterialInDb(courseId: null, ct);

        AuthenticateAsAdmin();
        Guid collectionId = await CreateCourseLevelCollectionAsync(courseId, ct);
        await AddMaterialToCollectionAsync(collectionId, materialId, ct);
        // НЕ публикуем подборку

        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(0, blueprint.TotalMaterials);
        Assert.Empty(blueprint.MaterialIds);
    }

    [Fact]
    public async Task Blueprint_Dedupes_MaterialInBothCourseAndCollection()
    {
        // Один и тот же material_id, привязанный к курсу и положенный в подборку курса —
        // считается один раз и в TotalMaterials, и в MaterialIds.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid materialId = await CreatePublishedMaterialInDb(courseId, ct);

        AuthenticateAsAdmin();
        Guid collectionId = await CreateCourseLevelCollectionAsync(courseId, ct);
        await AddMaterialToCollectionAsync(collectionId, materialId, ct);
        await PublishCollectionAsync(collectionId, ct);

        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds, materialId);
    }

    [Fact]
    public async Task Blueprint_Excludes_NonPublishedCourseMaterials()
    {
        // #496: знаменатели прогресса = только видимое студенту. DRAFT-материал,
        // привязанный к курсу, не должен попадать в TotalMaterials/MaterialIds —
        // иначе 100% и сертификат недостижимы.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid publishedId = await CreatePublishedMaterialInDb(courseId, ct);
        await CreateDraftMaterialInDb(courseId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds, publishedId);
    }

    [Fact]
    public async Task Blueprint_Excludes_DraftMaterialsInsidePublishedCollection()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid publishedId = await CreatePublishedMaterialInDb(courseId: null, ct);
        Guid draftId = await CreateDraftMaterialInDb(courseId: null, ct);

        AuthenticateAsAdmin();
        Guid collectionId = await CreateCourseLevelCollectionAsync(courseId, ct);
        await AddMaterialToCollectionAsync(collectionId, publishedId, ct);
        await AddMaterialToCollectionAsync(collectionId, draftId, ct);
        await PublishCollectionAsync(collectionId, ct);

        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds, publishedId);
    }

    [Fact]
    public async Task Blueprint_Excludes_NonPublishedModuleIssues()
    {
        // Прод-кейс #496: ARCHIVED-задача в модуле раздувала TotalUniqueIssues
        // (сайдбар «69 задач» против 68 видимых на странице программы).
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreateCourseInDb(ct);
        Guid projectId = await CreateProjectInDb(authorId, ct);
        Guid moduleId = await CreateModuleInDb(authorId, ct);
        await LinkModuleToCourseInDb(courseId, moduleId, ct);
        Guid publishedIssueId = await CreateIssueInDb(authorId, projectId, archive: false, ct);
        Guid archivedIssueId = await CreateIssueInDb(authorId, projectId, archive: true, ct);
        await LinkIssueToModuleInDb(moduleId, publishedIssueId, ct);
        await LinkIssueToModuleInDb(moduleId, archivedIssueId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalUniqueIssues);
    }

    [Fact]
    public async Task Blueprint_Excludes_NonPublishedProjectIssues()
    {
        // Симметричная project-ветка issue_refs (course_items → project_items):
        // ARCHIVED-задача проекта тоже не должна попадать в TotalUniqueIssues.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreateCourseInDb(ct);
        Guid projectId = await CreateProjectInDb(authorId, ct);
        await LinkProjectToCourseInDb(courseId, projectId, ct);
        Guid publishedIssueId = await CreateIssueInDb(authorId, projectId, archive: false, ct);
        Guid archivedIssueId = await CreateIssueInDb(authorId, projectId, archive: true, ct);
        await LinkIssueToProjectInDb(projectId, publishedIssueId, ct);
        await LinkIssueToProjectInDb(projectId, archivedIssueId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalUniqueIssues);
    }

    [Fact]
    public async Task Blueprint_NoMaterials_ReturnsEmptyArray()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(0, blueprint.TotalMaterials);
        Assert.NotNull(blueprint.MaterialIds);
        Assert.Empty(blueprint.MaterialIds);
        Assert.Equal(0, blueprint.TotalQuizzes);
        Assert.Empty(blueprint.QuizIds);
    }

    [Fact]
    public async Task Blueprint_Includes_PublishedQuizFromModuleItems()
    {
        // ST-13 #493: PUBLISHED-квиз из module_items модуля курса попадает в QuizIds,
        // TotalQuizzes и TotalItems.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid materialId = await CreatePublishedMaterialInDb(courseId, ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(courseId, ct);
        Guid quizId = await CreateQuizInDb(published: true, ct);
        await AttachQuizToModuleInDb(moduleId, quizId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalQuizzes);
        Assert.Single(blueprint.QuizIds, quizId);
        Assert.Equal(1, blueprint.TotalMaterials);
        Assert.Single(blueprint.MaterialIds, materialId);
        // TotalItems = материалы + задания + квизы.
        Assert.Equal(2, blueprint.TotalItems);
    }

    [Fact]
    public async Task Blueprint_Excludes_DraftQuiz()
    {
        // DRAFT-квиз студент пройти не может — он не должен блокировать 100%/сертификат.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(courseId, ct);
        Guid quizId = await CreateQuizInDb(published: false, ct);
        await AttachQuizToModuleInDb(moduleId, quizId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(0, blueprint.TotalQuizzes);
        Assert.Empty(blueprint.QuizIds);
        Assert.Equal(0, blueprint.TotalItems);
    }

    [Fact]
    public async Task Blueprint_Excludes_QuizBoundToCourseButNotInModuleItems()
    {
        // course_quizzes без module_item — привязка без позиции в программе: прогресс
        // считается по элементам программы, такой квиз в blueprint не входит.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid quizId = await CreateQuizInDb(published: true, ct);
        await ExecuteInDb(async db =>
        {
            db.Set<CourseQuiz>().Add(new CourseQuiz(courseId, quizId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(0, blueprint.TotalQuizzes);
        Assert.Empty(blueprint.QuizIds);
    }

    [Fact]
    public async Task Blueprint_Dedupes_QuizInTwoModulesOfSameCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid moduleAId = await CreateModuleAttachedToCourseInDb(courseId, ct);
        Guid moduleBId = await CreateModuleAttachedToCourseInDb(courseId, ct);
        Guid quizId = await CreateQuizInDb(published: true, ct);
        await AttachQuizToModuleInDb(moduleAId, quizId, ct);
        await AttachQuizToModuleInDb(moduleBId, quizId, ct);

        AuthenticateAsAdmin();
        CourseProgressBlueprintDto blueprint = await GetBlueprintAsync(courseId, ct);

        Assert.Equal(1, blueprint.TotalQuizzes);
        Assert.Single(blueprint.QuizIds, quizId);
    }

    private async Task<CourseProgressBlueprintDto> GetBlueprintAsync(Guid courseId, CancellationToken ct)
    {
        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/internal/progress/courses/blueprints",
            new GetCourseProgressBlueprintsRequest([courseId]),
            ct);
        resp.EnsureSuccessStatusCode();
        IReadOnlyCollection<CourseProgressBlueprintDto> list =
            await ReadResultAsync<IReadOnlyCollection<CourseProgressBlueprintDto>>(resp);
        return list.Single(b => b.CourseId == courseId);
    }

    private async Task<Guid> CreateCourseInDb(CancellationToken ct)
    {
        Guid courseId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId: Guid.NewGuid(),
                title: Title.Create("C").Value,
                description: Description.Create("D").Value,
                slug: CourseSlug.Create("c-" + Guid.NewGuid().ToString("N")[..8]).Value, SortKey.Initial());
            db.Set<Course>().Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });
        return courseId;
    }

    private async Task<Guid> CreatePublishedMaterialInDb(Guid? courseId, CancellationToken ct)
    {
        Guid materialId = Guid.NewGuid();
        string uniqueTitle = $"Mat-{Guid.NewGuid():N}".Substring(0, 32);
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: Guid.NewGuid(),
                title: Title.Create(uniqueTitle).Value,
                accessType: AccessType.PUBLIC);
            material.SetContent(MarkdownContent.Create("body").Value);
            UnitResult<Error> publish = material.Publish();
            Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);
            db.Set<Material>().Add(material);

            if (courseId is not null)
            {
                db.Set<CourseMaterial>().Add(new CourseMaterial(
                    courseId.Value, material.Id, SortKey.Initial()));
            }

            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }

    private async Task<Guid> CreateDraftMaterialInDb(Guid? courseId, CancellationToken ct)
    {
        Guid materialId = Guid.Empty;
        string uniqueTitle = $"Draft-{Guid.NewGuid():N}".Substring(0, 32);
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId: Guid.NewGuid(),
                title: Title.Create(uniqueTitle).Value,
                accessType: AccessType.PUBLIC);
            material.SetContent(MarkdownContent.Create("draft body").Value);
            db.Set<Material>().Add(material);

            if (courseId is not null)
            {
                db.Set<CourseMaterial>().Add(new CourseMaterial(
                    courseId.Value, material.Id, SortKey.Initial()));
            }

            await db.SaveChangesAsync(ct);
            materialId = material.Id;
        });
        return materialId;
    }

    private async Task<Guid> CreateProjectInDb(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var project = new Project(
                authorId,
                Title.Create($"P-{Guid.NewGuid():N}".Substring(0, 16)).Value,
                Description.Create("project description").Value);
            db.Set<Project>().Add(project);
            await db.SaveChangesAsync(ct);
            id = project.Id;
        });
        return id;
    }

    private async Task<Guid> CreateModuleInDb(Guid authorId, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create($"M-{Guid.NewGuid():N}".Substring(0, 16)).Value,
                Description.Create("module description").Value);
            db.Set<Module>().Add(module);
            await db.SaveChangesAsync(ct);
            id = module.Id;
        });
        return id;
    }

    private async Task<Guid> CreateIssueInDb(Guid authorId, Guid projectId, bool archive, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var issue = new Issue(
                authorId,
                projectId,
                Title.Create($"I-{Guid.NewGuid():N}".Substring(0, 16)).Value,
                MarkdownContent.Create("# issue body").Value);
            UnitResult<Error> publish = issue.Publish();
            Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);
            if (archive)
            {
                UnitResult<Error> archived = issue.Archive();
                Assert.True(archived.IsSuccess, archived.IsFailure ? archived.Error.GetMessage() : null);
            }

            db.Set<Issue>().Add(issue);
            await db.SaveChangesAsync(ct);
            id = issue.Id;
        });
        return id;
    }

    private Task LinkModuleToCourseInDb(Guid courseId, Guid moduleId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.Set<CourseItem>().Add(new CourseItem(
                courseId, CourseItemType.Module, moduleId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkProjectToCourseInDb(Guid courseId, Guid projectId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.Set<CourseItem>().Add(new CourseItem(
                courseId, CourseItemType.Project, projectId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkIssueToProjectInDb(Guid projectId, Guid issueId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.Set<ProjectItem>().Add(new ProjectItem(
                projectId, issueId, SortKey.Initial(), isOptional: false, maxScore: null));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkIssueToModuleInDb(Guid moduleId, Guid issueId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.Set<ModuleItem>().Add(new ModuleItem(
                moduleId, ModuleItemType.Issue, issueId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

    private async Task<Guid> CreateCourseLevelCollectionAsync(Guid courseId, CancellationToken ct)
    {
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Подборка курса", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(createResp);
    }

    private async Task AddMaterialToCollectionAsync(Guid collectionId, Guid materialId, CancellationToken ct)
    {
        HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        secResp.EnsureSuccessStatusCode();
        Guid sectionId = await ReadResultAsync<Guid>(secResp);

        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(materialId),
            ct);
        itemResp.EnsureSuccessStatusCode();
    }

    private async Task PublishCollectionAsync(Guid collectionId, CancellationToken ct)
    {
        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();
    }

    private async Task<Guid> CreateModuleAttachedToCourseInDb(Guid courseId, CancellationToken ct)
    {
        Guid moduleId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var module = new Module(authorId: Guid.NewGuid(), title: Title.Create("M").Value);
            db.Set<Module>().Add(module);
            db.Set<CourseItem>().Add(new CourseItem(
                courseId, CourseItemType.Module, module.Id, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
            moduleId = module.Id;
        });
        return moduleId;
    }

    private async Task<Guid> CreateQuizInDb(bool published, CancellationToken ct)
    {
        Guid quizId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            Guid optionA = Guid.NewGuid();
            QuizQuestion question = QuizQuestion.Create(
                Guid.NewGuid(),
                QuizQuestionType.SINGLE_CHOICE,
                "Вопрос квиза",
                [QuizOption.Create(optionA, "Вариант A").Value, QuizOption.Create(Guid.NewGuid(), "Вариант B").Value],
                [optionA],
                referenceAnswer: null).Value;

            Quiz quiz = Quiz.Create(Guid.NewGuid(), Title.Create("Quiz").Value, [question]).Value;
            if (published)
            {
                UnitResult<Error> publish = quiz.Publish();
                Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);
            }

            db.Set<Quiz>().Add(quiz);
            await db.SaveChangesAsync(ct);
            quizId = quiz.Id;
        });
        return quizId;
    }

    private async Task AttachQuizToModuleInDb(Guid moduleId, Guid quizId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.Set<ModuleItem>().Add(new ModuleItem(
                moduleId, ModuleItemType.Quiz, quizId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });
    }
}
