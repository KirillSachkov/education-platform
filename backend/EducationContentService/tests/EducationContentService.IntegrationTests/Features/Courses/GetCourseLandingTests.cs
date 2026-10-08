using System.Net;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class GetCourseLandingTests : EducationContentServiceTestsBase
{
    public GetCourseLandingTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetCourseLanding_Anonymous_PublishedCourse_ShouldReturnOk()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Published Landing", "Desc", publish: true, ct);

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/landing", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseLandingDto dto = await ReadResultAsync<CourseLandingDto>(response);
        Assert.Equal(courseId, dto.Id);
        Assert.Equal("Published Landing", dto.Title);
        Assert.Equal("PUBLISHED", dto.Status);
    }

    [Fact]
    public async Task GetCourseLanding_Anonymous_DraftCourse_ShouldReturn404()
    {
        // Arrange — draft course is hidden from anonymous callers (landing only serves PUBLISHED)
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("Draft Landing", "Desc", publish: false, ct);

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/landing", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseLanding_Author_OwnDraftCourse_ShouldReturn404()
    {
        // Landing is strictly public (PUBLISHED-only). Even the course's own author
        // cannot fetch their DRAFT course via /landing — authors use /curriculum or
        // /detail for draft preview. This test documents that intentional 404.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreateCourseInDbAsync(
            "Draft Own Landing", "Desc", publish: false, ct, authorId: authorId);

        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/landing", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseLanding_WithQuizModuleItems_ShouldReturnDistinctPublishedQuizCount()
    {
        // #551: stats.QuizCount = DISTINCT PUBLISHED-квизы из module_items модулей курса.
        // Один квиз в двух модулях считается один раз; DRAFT-квиз не считается.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid courseId = await CreateCourseInDbAsync("Quiz Landing", "Desc", publish: true, ct, authorId);
        Guid moduleAId = await AddPublishedModuleToCourseAsync(courseId, authorId);
        Guid moduleBId = await AddPublishedModuleToCourseAsync(courseId, authorId);

        Guid publishedQuizId = await CreateQuizInDbAsync(authorId, publish: true);
        Guid draftQuizId = await CreateQuizInDbAsync(authorId, publish: false);

        await AddQuizModuleItemAsync(moduleAId, publishedQuizId);
        await AddQuizModuleItemAsync(moduleBId, publishedQuizId); // тот же квиз во втором модуле — не дублируется
        await AddQuizModuleItemAsync(moduleAId, draftQuizId); // DRAFT — не входит в счётчик

        RemoveAuthentication();

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/landing", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        CourseLandingDto dto = await ReadResultAsync<CourseLandingDto>(response);
        Assert.Equal(1, dto.Stats.QuizCount);
        Assert.Equal(2, dto.Stats.ModuleCount);
    }

    [Fact]
    public async Task GetCourseLanding_WithoutQuizzes_ShouldReturnZeroQuizCount()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDbAsync("No Quiz Landing", "Desc", publish: true, ct);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/landing", ct);

        response.EnsureSuccessStatusCode();
        CourseLandingDto dto = await ReadResultAsync<CourseLandingDto>(response);
        Assert.Equal(0, dto.Stats.QuizCount);
    }

    [Fact]
    public async Task GetCourseLanding_NonExistentCourse_ShouldReturn404()
    {
        CancellationToken ct = CancellationToken.None;

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{Guid.NewGuid()}/landing", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseInDbAsync(
        string title, string description, bool publish, CancellationToken ct, Guid? authorId = null)
    {
        Guid courseId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            string slug = $"landing-{Guid.NewGuid().ToString("N")[..8]}";
            var course = new Course(
                authorId ?? Guid.CreateVersion7(),
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

    private async Task<Guid> AddPublishedModuleToCourseAsync(Guid courseId, Guid authorId)
    {
        Guid moduleId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create($"Модуль {Guid.NewGuid():N}").Value,
                Description.Create("Описание модуля").Value);
            module.Publish();
            db.Modules.Add(module);
            moduleId = module.Id;

            string? lastSortKey = await db.CourseItems
                .Where(ci => ci.CourseId == courseId)
                .OrderByDescending(ci => ci.SortKey)
                .Select(ci => ci.SortKey.Value)
                .FirstOrDefaultAsync();

            SortKey nextKey = lastSortKey is null
                ? SortKey.Initial()
                : SortKey.After(SortKey.Create(lastSortKey).Value);

            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, moduleId, nextKey, isOptional: false));
            await db.SaveChangesAsync();
        });

        return moduleId;
    }

    private async Task<Guid> CreateQuizInDbAsync(Guid authorId, bool publish)
    {
        Guid quizId = Guid.Empty;
        Guid optionA = Guid.NewGuid();
        Guid optionB = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create($"Квиз {Guid.NewGuid():N}").Value,
                [
                    QuizQuestion.Create(
                        Guid.NewGuid(),
                        QuizQuestionType.SINGLE_CHOICE,
                        "Что такое CLR?",
                        [
                            QuizOption.Create(optionA, "Среда выполнения").Value,
                            QuizOption.Create(optionB, "Компилятор").Value,
                        ],
                        [optionA],
                        referenceAnswer: null).Value,
                ]).Value;

            if (publish)
                quiz.Publish();

            db.Quizzes.Add(quiz);
            quizId = quiz.Id;
            await db.SaveChangesAsync();
        });

        return quizId;
    }

    private async Task AddQuizModuleItemAsync(Guid moduleId, Guid quizId)
    {
        await ExecuteInDb(async db =>
        {
            string? lastSortKey = await db.ModuleItems
                .Where(mi => mi.ModuleId == moduleId)
                .OrderByDescending(mi => mi.SortKey)
                .Select(mi => mi.SortKey.Value)
                .FirstOrDefaultAsync();

            SortKey nextKey = lastSortKey is null
                ? SortKey.Initial()
                : SortKey.After(SortKey.Create(lastSortKey).Value);

            db.ModuleItems.Add(new ModuleItem(
                moduleId, ModuleItemType.Quiz, quizId, nextKey, isOptional: false));
            await db.SaveChangesAsync();
        });
    }
}
