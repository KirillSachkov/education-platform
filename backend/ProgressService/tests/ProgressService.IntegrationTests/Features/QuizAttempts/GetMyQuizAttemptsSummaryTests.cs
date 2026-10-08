using System.Net;
using System.Net.Http.Json;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.QuizAttempts;

/// <summary>
///     Страница «Мои тесты» (#556): агрегат всех попыток текущего пользователя по
///     квизам — лучший/последний балл, число попыток, итоговый pass; исключает
///     LEVEL_TEST-квизы и квизы без ECS-summary; пусто без попыток.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyQuizAttemptsSummaryTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";
    private const string LEVEL_TEST = "LEVEL_TEST";
    private const int PASSING_SCORE = 70;
    private const string SUMMARY_URL = "/progress/quizzes/attempts/my-summary";

    private static readonly Guid _q1 = Guid.NewGuid();
    private static readonly Guid _q1Correct = Guid.NewGuid();
    private static readonly Guid _q1Wrong = Guid.NewGuid();

    public GetMyQuizAttemptsSummaryTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Summary_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(SUMMARY_URL);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Summary_NoAttempts_ReturnsEmpty()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        Assert.Empty(summary.Items);
        Assert.Equal(0, summary.TotalQuizzesTaken);
        Assert.Equal(0, summary.PassedCount);
        Assert.Equal(0, summary.AvgBestScorePercent);
    }

    [Fact]
    public async Task Summary_GroupsAttemptsPerQuiz_BestLastAttemptsPassed()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid quizId = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(quizId, "Тест по основам", courseId: courseId);

        AuthenticateAs(userId, "platform-participant");

        // Попытка 1 — провал (0%).
        await SubmitAsync(quizId, _q1Wrong);
        // Попытка 2 — успех (100%), последняя по времени.
        await SubmitAsync(quizId, _q1Correct);

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        Assert.Equal(1, summary.TotalQuizzesTaken);
        Assert.Equal(1, summary.PassedCount);
        Assert.Equal(100, summary.AvgBestScorePercent);

        MyQuizAttemptsSummaryItem item = Assert.Single(summary.Items);
        Assert.Equal(quizId, item.QuizId);
        Assert.Equal("Тест по основам", item.Title);
        Assert.Equal(courseId, item.CourseId);
        Assert.Equal(2, item.AttemptsCount);
        Assert.Equal(100, item.BestScorePercent);
        Assert.Equal(100, item.LastScorePercent);
        Assert.True(item.Passed);
    }

    [Fact]
    public async Task Summary_NeverPassedQuiz_PassedFalse()
    {
        Guid userId = Guid.NewGuid();
        Guid quizId = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(quizId, "Сложный тест");

        AuthenticateAs(userId, "platform-participant");
        await SubmitAsync(quizId, _q1Wrong);

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        MyQuizAttemptsSummaryItem item = Assert.Single(summary.Items);
        Assert.False(item.Passed);
        Assert.Equal(0, item.BestScorePercent);
        Assert.Equal(0, summary.PassedCount);
        Assert.Null(item.CourseId);
    }

    [Fact]
    public async Task Summary_ExcludesLevelTestQuizzes()
    {
        Guid userId = Guid.NewGuid();
        Guid materialQuizId = SeedSingleChoiceQuiz();
        Guid levelTestQuizId = Guid.NewGuid();
        EducationContentClient.AddQuizSummary(materialQuizId, "Обычный тест");
        EducationContentClient.AddQuizSummary(levelTestQuizId, "Определи свой уровень", LEVEL_TEST);

        // Обычный сабмит LEVEL_TEST-квиза запрещён (своя воронка) — попытку сеем напрямую.
        await SeedAttemptAsync(userId, levelTestQuizId, scorePercent: 90, passed: true);

        AuthenticateAs(userId, "platform-participant");
        await SubmitAsync(materialQuizId, _q1Correct);

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        Assert.Equal(1, summary.TotalQuizzesTaken);
        MyQuizAttemptsSummaryItem item = Assert.Single(summary.Items);
        Assert.Equal(materialQuizId, item.QuizId);
    }

    [Fact]
    public async Task Summary_QuizWithoutEcsSummary_DroppedFromResult()
    {
        // Квиз hard-deleted в ECS → GetQuizSummaries не вернёт его, попытка отбрасывается.
        Guid userId = Guid.NewGuid();
        Guid withSummary = SeedSingleChoiceQuiz();
        Guid deletedQuiz = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(withSummary, "Живой тест");
        // deletedQuiz — намеренно НЕ регистрируем summary.

        AuthenticateAs(userId, "platform-participant");
        await SubmitAsync(withSummary, _q1Correct);
        await SubmitAsync(deletedQuiz, _q1Correct);

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        MyQuizAttemptsSummaryItem item = Assert.Single(summary.Items);
        Assert.Equal(withSummary, item.QuizId);
    }

    [Fact]
    public async Task Summary_DoesNotReturnOtherUsersAttempts()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(quizId, "Чужой тест");

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        await SubmitAsync(quizId, _q1Correct);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        Assert.Empty(summary.Items);
    }

    [Fact]
    public async Task Summary_OrdersByLastActivityDescending()
    {
        Guid userId = Guid.NewGuid();
        Guid firstQuiz = SeedSingleChoiceQuiz();
        Guid secondQuiz = SeedSingleChoiceQuiz();
        EducationContentClient.AddQuizSummary(firstQuiz, "Первый тест");
        EducationContentClient.AddQuizSummary(secondQuiz, "Второй тест");

        AuthenticateAs(userId, "platform-participant");
        await SubmitAsync(firstQuiz, _q1Correct);
        await SubmitAsync(secondQuiz, _q1Correct);

        MyQuizAttemptsSummaryResponse summary = await GetSummaryAsync();

        Assert.Equal(2, summary.Items.Count);
        // Второй квиз сабмитнут позже → стоит первым.
        Assert.Equal(secondQuiz, summary.Items[0].QuizId);
        Assert.Equal(firstQuiz, summary.Items[1].QuizId);
    }

    private Guid SeedSingleChoiceQuiz(string purpose = MATERIAL_CHECK)
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new EducationContentService.Contracts.Quizzes.QuizAnswerKeyDto(
            quizId,
            purpose,
            PASSING_SCORE,
            [new EducationContentService.Contracts.Quizzes.QuizAnswerKeyQuestionDto(
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

    private async Task SubmitAsync(Guid quizId, Guid selectedOption)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/quizzes/{quizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [selectedOption], null)]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<MyQuizAttemptsSummaryResponse> GetSummaryAsync()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(SUMMARY_URL);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadWrappedResultAsync<MyQuizAttemptsSummaryResponse>(response);
    }
}
