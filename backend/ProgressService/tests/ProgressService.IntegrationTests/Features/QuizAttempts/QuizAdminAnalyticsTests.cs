using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Quizzes;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.QuizAttempts;

/// <summary>
///     Админ-аналитика по всем тестам (#556, AC5): overview агрегирует попытки по квизам,
///     исключает LEVEL_TEST, требует Users.VIEW (403 для участника); drill-in считает
///     per-вопрос долю верных + распределение баллов из сохранённых ответов.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class QuizAdminAnalyticsTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";
    private const string LEVEL_TEST = "LEVEL_TEST";
    private const int PASSING_SCORE = 70;
    private const string OVERVIEW_URL = "/progress/quizzes/admin/overview";

    private static readonly Guid _q1 = Guid.NewGuid();
    private static readonly Guid _q1Correct = Guid.NewGuid();
    private static readonly Guid _q1Wrong = Guid.NewGuid();

    public QuizAdminAnalyticsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Overview_ForParticipant_ReturnsForbidden()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(OVERVIEW_URL);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overview_AggregatesAcrossQuizzes_ExcludesLevelTest()
    {
        Guid courseId = Guid.NewGuid();
        Guid quizA = SeedSingleChoiceQuiz();
        Guid quizB = SeedSingleChoiceQuiz();
        Guid levelTest = SeedSingleChoiceQuiz(LEVEL_TEST);

        EducationContentClient.AddQuizSummary(quizA, "Тест A", MATERIAL_CHECK, courseId);
        EducationContentClient.AddQuizSummary(quizB, "Тест B");
        EducationContentClient.AddQuizSummary(levelTest, "Определи уровень", LEVEL_TEST);
        EducationContentClient.AddCourseTitle(courseId, "Курс .NET");

        // quizA: 2 пользователя — один прошёл (100), один нет (40).
        Guid userA1 = Guid.NewGuid();
        Guid userA2 = Guid.NewGuid();
        await SeedAttemptAsync(userA1, quizA, scorePercent: 100, passed: true);
        await SeedAttemptAsync(userA2, quizA, scorePercent: 40, passed: false);
        // quizB: один пользователь, провал.
        await SeedAttemptAsync(Guid.NewGuid(), quizB, scorePercent: 50, passed: false);
        // level-test: попытка есть, но в overview не попадает.
        await SeedAttemptAsync(Guid.NewGuid(), levelTest, scorePercent: 90, passed: true);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        Envelope<QuizAdminOverviewResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<QuizAdminOverviewResponse>>(OVERVIEW_URL);

        QuizAdminOverviewResponse overview = envelope!.Result!;

        Assert.Equal(2, overview.TotalQuizzes);
        Assert.Equal(3, overview.TotalAttempts);
        // 1 из 3 попыток прошла → 33.3%.
        Assert.Equal(33.3, overview.OverallPassRatePercent);
        // (100 + 40 + 50) / 3 = 63.3.
        Assert.Equal(63.3, overview.OverallAvgScorePercent);

        // LEVEL_TEST не должен фигурировать.
        Assert.DoesNotContain(overview.Quizzes, q => q.QuizId == levelTest);

        // Самый «попыточный» квиз — первым (quizA: 2 попытки).
        QuizAdminOverviewRow rowA = overview.Quizzes[0];
        Assert.Equal(quizA, rowA.QuizId);
        Assert.Equal("Тест A", rowA.Title);
        Assert.Equal(courseId, rowA.CourseId);
        Assert.Equal("Курс .NET", rowA.CourseTitle);
        Assert.Equal(2, rowA.AttemptsCount);
        Assert.Equal(2, rowA.UniqueUsers);
        Assert.Equal(50, rowA.PassRatePercent);
        Assert.Equal(70, rowA.AvgScorePercent);

        QuizAdminOverviewRow rowB = overview.Quizzes[1];
        Assert.Equal(quizB, rowB.QuizId);
        Assert.Null(rowB.CourseId);
        Assert.Null(rowB.CourseTitle);
        Assert.Equal(1, rowB.AttemptsCount);
        Assert.Equal(0, rowB.PassRatePercent);
    }

    [Fact]
    public async Task Overview_NoAttempts_ReturnsEmpty()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        Envelope<QuizAdminOverviewResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<QuizAdminOverviewResponse>>(OVERVIEW_URL);

        QuizAdminOverviewResponse overview = envelope!.Result!;
        Assert.Empty(overview.Quizzes);
        Assert.Equal(0, overview.TotalQuizzes);
        Assert.Equal(0, overview.TotalAttempts);
    }

    [Fact]
    public async Task Stats_ComputesPerQuestionCorrectRate_AndScoreDistribution()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(quizId, "Тест по основам");

        // Три попытки: два верных ответа (100), один неверный (0).
        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Correct, scorePercent: 100, passed: true);
        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Correct, scorePercent: 100, passed: true);
        await SeedAttemptWithAnswerAsync(Guid.NewGuid(), quizId, _q1Wrong, scorePercent: 0, passed: false);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        Envelope<QuizAdminStatsResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<QuizAdminStatsResponse>>($"/progress/quizzes/admin/{quizId}/stats");

        QuizAdminStatsResponse stats = envelope!.Result!;

        Assert.Equal(quizId, stats.QuizId);
        Assert.Equal("Тест по основам", stats.Title);
        Assert.Equal(3, stats.AttemptsCount);
        Assert.Equal(3, stats.UniqueUsers);

        // Score distribution: два в 81-100, один в 0-20.
        Assert.Equal(1, stats.ScoreDistribution.Single(b => b.Bucket == "0-20").Count);
        Assert.Equal(2, stats.ScoreDistribution.Single(b => b.Bucket == "81-100").Count);
        Assert.Equal(5, stats.ScoreDistribution.Count);

        // Один вопрос: 3 ответили, 2 верных → 66.7%.
        QuizQuestionStatsRow question = Assert.Single(stats.Questions);
        Assert.Equal(_q1, question.QuestionId);
        Assert.Equal("Вопрос 1", question.Text);
        Assert.Equal(3, question.AnsweredCount);
        Assert.Equal(2, question.CorrectCount);
        Assert.Equal(66.7, question.CorrectRatePercent);
    }

    [Fact]
    public async Task Stats_ForParticipant_ReturnsForbidden()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/progress/quizzes/admin/{quizId}/stats");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Stats_AnswerKeyNotFound_Returns404()
    {
        Guid quizId = Guid.NewGuid(); // answer-key намеренно не регистрируем.
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        HttpResponseMessage response =
            await AppHttpClient.GetAsync($"/progress/quizzes/admin/{quizId}/stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private Guid SeedSingleChoiceQuiz(string purpose = MATERIAL_CHECK)
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            purpose,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(
                _q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null)],
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
