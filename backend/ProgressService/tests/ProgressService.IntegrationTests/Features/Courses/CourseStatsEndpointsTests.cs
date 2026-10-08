using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Quizzes;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.Courses;

/// <summary>
///     Author per-course аналитика (#634): тесты курса + прогресс курса, под ownership-гейтом.
///     Овервью агрегирует только квизы ЭТОГО курса (blueprint.QuizIds), LEVEL_TEST/чужие
///     квизы исключены; drill-in отвергает квиз вне курса (anti-IDOR 404); прогресс считает
///     вовлечённость + три author-счётчика. Не-владелец курса → 403.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseStatsEndpointsTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";
    private const int PASSING_SCORE = 70;

    private static readonly Guid _q1 = Guid.NewGuid();
    private static readonly Guid _q1Correct = Guid.NewGuid();
    private static readonly Guid _q1Wrong = Guid.NewGuid();

    public CourseStatsEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task QuizOverview_AsCourseOwner_AggregatesOnlyCourseQuizzes()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid quizA = SeedSingleChoiceQuiz();
        Guid quizB = SeedSingleChoiceQuiz();
        Guid quizOther = SeedSingleChoiceQuiz(); // тест ДРУГОГО курса — в blueprint не входит.

        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Курс .NET", "desc", quizIds: [quizA, quizB], totalQuizzes: 2);
        EducationContentClient.AddQuizSummary(quizA, "Тест A");
        EducationContentClient.AddQuizSummary(quizB, "Тест B");
        EducationContentClient.AddQuizSummary(quizOther, "Чужой тест");

        // quizA: 2 пользователя (100 прошёл, 40 нет); quizB: 1 провал; quizOther: попытка вне курса.
        await SeedAttemptAsync(Guid.NewGuid(), quizA, scorePercent: 100, passed: true);
        await SeedAttemptAsync(Guid.NewGuid(), quizA, scorePercent: 40, passed: false);
        await SeedAttemptAsync(Guid.NewGuid(), quizB, scorePercent: 50, passed: false);
        await SeedAttemptAsync(Guid.NewGuid(), quizOther, scorePercent: 90, passed: true);

        AuthenticateAs(authorId, "platform-author");
        Envelope<CourseQuizStatsOverviewResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<CourseQuizStatsOverviewResponse>>(
                $"/progress/courses/{courseId}/stats/quizzes/");

        CourseQuizStatsOverviewResponse overview = envelope!.Result!;

        Assert.Equal(courseId, overview.CourseId);
        Assert.Equal(2, overview.TotalQuizzesWithAttempts);
        Assert.Equal(3, overview.TotalAttempts); // quizOther исключён.
        Assert.Equal(33.3, overview.OverallPassRatePercent, precision: 1); // 1 из 3.
        Assert.Equal(63.3, overview.OverallAvgScorePercent, precision: 1); // (100+40+50)/3.
        Assert.DoesNotContain(overview.Quizzes, q => q.QuizId == quizOther);

        CourseQuizStatsRow rowA = overview.Quizzes[0]; // больше попыток — первым.
        Assert.Equal(quizA, rowA.QuizId);
        Assert.Equal("Тест A", rowA.Title);
        Assert.Equal(2, rowA.AttemptsCount);
        Assert.Equal(2, rowA.UniqueUsers);
        Assert.Equal(50, rowA.PassRatePercent, precision: 1);
        Assert.Equal(70, rowA.AvgScorePercent, precision: 1);
    }

    [Fact]
    public async Task QuizOverview_AsNonOwnerAuthor_ReturnsForbidden()
    {
        Guid ownerId = Guid.NewGuid();
        Guid strangerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: ownerId);
        EducationContentClient.AddCourseBlueprint(courseId, "Курс", "desc");

        AuthenticateAs(strangerId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/stats/quizzes/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task QuizOverview_CourseWithoutQuizzes_ReturnsEmpty()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddCourseBlueprint(courseId, "Курс", "desc"); // quizIds: [] по умолчанию.

        AuthenticateAs(authorId, "platform-author");
        Envelope<CourseQuizStatsOverviewResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<CourseQuizStatsOverviewResponse>>(
                $"/progress/courses/{courseId}/stats/quizzes/");

        CourseQuizStatsOverviewResponse overview = envelope!.Result!;
        Assert.Empty(overview.Quizzes);
        Assert.Equal(0, overview.TotalQuizzesWithAttempts);
        Assert.Equal(0, overview.TotalAttempts);
    }

    [Fact]
    public async Task QuizDrillIn_AsCourseOwner_ReturnsPerQuestionStats()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid quizId = SeedSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddCourseBlueprint(courseId, "Курс", "desc", quizIds: [quizId], totalQuizzes: 1);
        EducationContentClient.AddQuizSummary(quizId, "Тест по основам");

        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Correct, scorePercent: 100, passed: true);
        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Correct, scorePercent: 100, passed: true);
        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Wrong, scorePercent: 0, passed: false);

        AuthenticateAs(authorId, "platform-author");
        Envelope<QuizAdminStatsResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<QuizAdminStatsResponse>>(
                $"/progress/courses/{courseId}/stats/quizzes/{quizId}/");

        QuizAdminStatsResponse stats = envelope!.Result!;
        Assert.Equal(quizId, stats.QuizId);
        Assert.Equal(3, stats.AttemptsCount);
        QuizQuestionStatsRow question = Assert.Single(stats.Questions);
        Assert.Equal(3, question.AnsweredCount);
        Assert.Equal(2, question.CorrectCount);
        Assert.Equal(66.7, question.CorrectRatePercent, precision: 1);
    }

    [Fact]
    public async Task QuizDrillIn_QuizNotInCourse_ReturnsNotFound()
    {
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid courseQuiz = SeedSingleChoiceQuiz();
        Guid foreignQuiz = SeedSingleChoiceQuiz(); // существует, но не в этом курсе.

        EducationContentClient.AddCourse(courseId, authorId: authorId);
        EducationContentClient.AddCourseBlueprint(courseId, "Курс", "desc", quizIds: [courseQuiz], totalQuizzes: 1);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/stats/quizzes/{foreignQuiz}/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ProgressStats_AsCourseOwner_CountsEngagementAndActivity()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid quizId = SeedSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 1);
        EducationContentClient.AddMaterial(moduleId, materialId, 1);
        EducationContentClient.AddMaterialCourseContext(materialId, courseId, moduleId, moduleItemsTotal: 1);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Курс", "desc",
            totalMaterials: 1, totalUniqueIssues: 1, totalQuizzes: 1,
            materialIds: [materialId], quizIds: [quizId]);

        // Студент (как admin — обход entitlement) генерирует реальную активность.
        AuthenticateAs(studentId, "platform-admin");
        await SeedEnrollmentAsync(courseId, studentId, authorId);
        await PostAsync($"/progress/materials/{materialId}/view");
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        await SeedAttemptAsync(studentId, quizId, scorePercent: 100, passed: true);

        AuthenticateAs(authorId, "platform-author");
        Envelope<CourseProgressStatsResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<CourseProgressStatsResponse>>(
                $"/progress/courses/{courseId}/stats/progress/");

        CourseProgressStatsResponse stats = envelope!.Result!;
        Assert.Equal(1, stats.EngagedStudents);
        Assert.Equal(1, stats.CompletedStudents); // 1/1 материал = 100% ≥ 80%.
        Assert.Equal(100, stats.AverageProgressPercent, precision: 1);
        Assert.Equal(1, stats.MaterialViewsCompleted);
        Assert.Equal(1, stats.IssueSubmissionsCount);
        Assert.Equal(1, stats.QuizPassersCount);
    }

    [Fact]
    public async Task ProgressStats_AsNonOwnerAuthor_ReturnsForbidden()
    {
        Guid ownerId = Guid.NewGuid();
        Guid strangerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: ownerId);
        EducationContentClient.AddCourseBlueprint(courseId, "Курс", "desc");

        AuthenticateAs(strangerId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/stats/progress/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Guid SeedSingleChoiceQuiz(string purpose = MATERIAL_CHECK)
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            purpose,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null)],
            LevelTestConfig: null));
        return quizId;
    }

    private async Task SeedAttemptAsync(Guid userId, Guid quizId, int scorePercent, bool passed)
    {
        await ExecuteInDb(async db =>
        {
            QuizAttempt attempt = QuizAttempt.Create(userId, quizId, [], scorePercent, passed).Value;
            await db.QuizAttempts.AddAsync(attempt);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedAttemptWithAnswerAsync(
        Guid userId,
        Guid quizId,
        Guid selectedOption,
        int scorePercent,
        bool passed)
    {
        await ExecuteInDb(async db =>
        {
            QuizAttemptAnswer answer = QuizAttemptAnswer.Create(_q1, [selectedOption], null).Value;
            QuizAttempt attempt = QuizAttempt.Create(userId, quizId, [answer], scorePercent, passed).Value;
            await db.QuizAttempts.AddAsync(attempt);
            await db.SaveChangesAsync();
        });
    }
}
