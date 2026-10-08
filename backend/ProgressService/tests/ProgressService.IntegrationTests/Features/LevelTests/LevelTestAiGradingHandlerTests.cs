using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Quizzes;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Features.LevelTests.AiGrading;
using ProgressService.Core.Features.LevelTests.IntegrationEvents;
using ProgressService.Domain.LevelTests;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.LevelTests;

/// <summary>
///     L1-тесты Wolverine-handler'а AI-грейдинга level-test попыток (ST-5, #480):
///     сабмит через HTTP → ручной <c>InvokeMessageAndWaitAsync(GradeLevelTestAttemptRequested)</c>
///     (outbox в тестах не доставляет) → проверка статусов/пересчёта через GET-результат.
///     AI seam — <see cref="FakeLevelTestGradeExtractor"/> (скриптуется per-test).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class LevelTestAiGradingHandlerTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string OPEN_TEXT = "OPEN_TEXT";
    private const string LEVEL_TEST = "LEVEL_TEST";

    private const string SUBMIT_URL = "/progress/level-test/attempts";
    private const string CLAIM_URL = "/progress/level-test/attempts/claim";

    // basics: один choice JUNIOR (1 балл). web: два open_text — MIDDLE (2) + SENIOR (3).
    private static readonly Guid _choice = Guid.NewGuid();
    private static readonly Guid _open1 = Guid.NewGuid();
    private static readonly Guid _open2 = Guid.NewGuid();
    private static readonly Guid _choiceCorrect = Guid.NewGuid();

    private readonly IntegrationTestsWebFactory _factory;

    public LevelTestAiGradingHandlerTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Grade_Success_RecomputesWithOpenTextInDenominators_TeaserStaysTeaser()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        RemoveAuthentication();
        string anonymousId = Guid.NewGuid().ToString();

        // Анонимный сабмит с двумя открытыми ответами: choice-only baseline —
        // web-секция (только open_text) исключена из знаменателей → overall 100.
        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, anonymousId, AnsweredAll()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptTeaserResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(submitResponse);
        Assert.Equal(100, submitted.OverallPercent);
        Assert.Equal("QUEUED", submitted.AiGradingStatus);

        _factory.GradeExtractor.QueueGrades(
            new LevelTestAiGradeItem(_open1, 50, "Суть DI схвачена, не хватает scope'ов."),
            new LevelTestAiGradeItem(_open2, 100, "Полный ответ про middleware-пайплайн."));

        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        Assert.Equal(1, _factory.GradeExtractor.Invocations);

        // Промпт собран из answer-key (текст вопроса + эталон) и ответа кандидата.
        string prompt = _factory.GradeExtractor.ReceivedRequests.Single().UserText;
        Assert.Contains("Объясните, что такое DI", prompt, StringComparison.Ordinal);
        Assert.Contains("Эталон про DI-контейнер", prompt, StringComparison.Ordinal);
        Assert.Contains("Мой ответ про DI", prompt, StringComparison.Ordinal);
        Assert.Contains(_open2.ToString(), prompt, StringComparison.Ordinal);

        // Неклеймленная попытка остаётся lead-gated тизером — но уже с READY и
        // пересчитанным overall: web = (1.0 + 3.0)/5 = 80%, overall = (100+80)/2 = 90.
        HttpResponseMessage teaserResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        Assert.Equal(HttpStatusCode.OK, teaserResponse.StatusCode);
        string teaserJson = await teaserResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"sections\"", teaserJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"questions\"", teaserJson, StringComparison.OrdinalIgnoreCase);
        LevelTestAttemptTeaserResponse teaser =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(teaserResponse);
        Assert.Equal("READY", teaser.AiGradingStatus);
        Assert.Equal(90, teaser.OverallPercent);

        // После клейма владелец видит полный разбор с AI-полями.
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");
        HttpResponseMessage claimResponse = await AppHttpClient.PostAsJsonAsync(
            CLAIM_URL,
            new ClaimLevelTestAttemptsRequest(anonymousId));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);

        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(resultResponse);

        Assert.Equal("READY", result.AiGradingStatus);
        Assert.Equal(90, result.OverallPercent);
        Assert.Equal("SENIOR", result.Level);

        LevelTestSectionScoreResponse web = result.Sections.Single(s => s.Key == "web");
        Assert.Equal(80, web.Percent);
        Assert.Equal(4m, web.EarnedPoints);
        Assert.Equal(5m, web.MaxPoints);

        LevelTestQuestionResultResponse graded1 = result.Questions.Single(q => q.QuestionId == _open1);
        Assert.False(graded1.PendingAi);
        Assert.Equal(50, graded1.AiScore);
        Assert.Equal("Суть DI схвачена, не хватает scope'ов.", graded1.AiFeedback);
        Assert.Equal(1m, graded1.EarnedPoints);

        LevelTestQuestionResultResponse graded2 = result.Questions.Single(q => q.QuestionId == _open2);
        Assert.Equal(100, graded2.AiScore);
        Assert.Equal(3m, graded2.EarnedPoints);
    }

    [Fact]
    public async Task Grade_AiThrows_RetriesConfiguredAttempts_ThenMarksFailed_ChoiceOnlyIntact()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, AnsweredAll()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptResultResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        _factory.GradeExtractor.QueueThrowSticky(new HttpRequestException("AI провайдер лёг"));

        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        // MaxAttempts = 3 (дефолт), RetryDelaySeconds = 0 в тестовом конфиге.
        Assert.Equal(3, _factory.GradeExtractor.Invocations);

        LevelTestAttempt stored = await ExecuteInDb(async db => await db.LevelTestAttempts.SingleAsync());
        Assert.Equal(LevelTestAiGradingStatus.FAILED, stored.AiGradingStatus);

        // Choice-only результат не тронут, pending-ответы остаются помеченными.
        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(resultResponse);
        Assert.Equal("FAILED", result.AiGradingStatus);
        Assert.Equal(100, result.OverallPercent);
        LevelTestQuestionResultResponse open1 = result.Questions.Single(q => q.QuestionId == _open1);
        Assert.True(open1.PendingAi);
        Assert.Null(open1.AiScore);
        Assert.Equal(0m, open1.EarnedPoints);
    }

    [Fact]
    public async Task Grade_DisabledByConfig_MarksFailedImmediately_WithoutAiCall()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, AnsweredAll()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptResultResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        _factory.LevelTestAiEnabled = false;

        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        Assert.Equal(0, _factory.GradeExtractor.Invocations);

        LevelTestAttempt stored = await ExecuteInDb(async db => await db.LevelTestAttempts.SingleAsync());
        Assert.Equal(LevelTestAiGradingStatus.FAILED, stored.AiGradingStatus);
    }

    [Fact]
    public async Task Grade_AlreadyReady_SkipsIdempotently_WithoutAiCall()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, AnsweredAll()));
        LevelTestAttemptResultResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        _factory.GradeExtractor.QueueGrades(
            new LevelTestAiGradeItem(_open1, 50, "Ок."),
            new LevelTestAiGradeItem(_open2, 100, "Ок."));
        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        // Повторная доставка сообщения (replay) на READY-попытке: AI не вызывается,
        // результат не меняется. Фейк без заскриптованного ответа бросил бы исключение.
        _factory.GradeExtractor.Reset();
        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        Assert.Equal(0, _factory.GradeExtractor.Invocations);

        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(resultResponse);
        Assert.Equal("READY", result.AiGradingStatus);
        Assert.Equal(90, result.OverallPercent);
    }

    [Fact]
    public async Task Grade_PartialAiResponse_AppliesSubset_RestStaysPending()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, AnsweredAll()));
        LevelTestAttemptResultResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        // AI вернул оценку только для _open1 — частичный результат применяется,
        // _open2 остаётся PendingAi с 0 заработанных, но входит в знаменатель READY.
        _factory.GradeExtractor.QueueGrades(new LevelTestAiGradeItem(_open1, 50, "Половина."));

        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(resultResponse);

        Assert.Equal("READY", result.AiGradingStatus);
        // web = (2×0.5 + 0)/5 = 20%, overall = (100×1 + 20×1)/2 = 60 → MIDDLE.
        Assert.Equal(60, result.OverallPercent);
        Assert.Equal("MIDDLE", result.Level);

        LevelTestQuestionResultResponse ungraded = result.Questions.Single(q => q.QuestionId == _open2);
        Assert.True(ungraded.PendingAi);
        Assert.Null(ungraded.AiScore);
        Assert.Equal(0m, ungraded.EarnedPoints);
        Assert.Equal(3m, ungraded.MaxPoints);
    }

    [Fact]
    public async Task Grade_AnswerKeyUnavailable_RetriesThenMarksFailed_WithoutAiCall()
    {
        Guid quizId = SeedAiLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, AnsweredAll()));
        LevelTestAttemptResultResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        EducationContentClient.SetQuizAnswerKeysUnavailable();

        await InvokeMessageAndWaitAsync(new GradeLevelTestAttemptRequested(submitted.AttemptId));

        Assert.Equal(0, _factory.GradeExtractor.Invocations);

        LevelTestAttempt stored = await ExecuteInDb(async db => await db.LevelTestAttempts.SingleAsync());
        Assert.Equal(LevelTestAiGradingStatus.FAILED, stored.AiGradingStatus);
    }

    private Guid SeedAiLevelTestQuiz()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            LEVEL_TEST,
            PassingScorePercent: 0,
            [
                new QuizAnswerKeyQuestionDto(
                    _choice, SINGLE_CHOICE, "Что такое CLR?", "basics", "JUNIOR", [_choiceCorrect], null),
                new QuizAnswerKeyQuestionDto(
                    _open1, OPEN_TEXT, "Объясните, что такое DI", "web", "MIDDLE", [], "Эталон про DI-контейнер"),
                new QuizAnswerKeyQuestionDto(
                    _open2, OPEN_TEXT, "Объясните middleware-пайплайн", "web", "SENIOR", [], null),
            ],
            new LevelTestConfigDto(
                [
                    new LevelThresholdDto("JUNIOR", 0),
                    new LevelThresholdDto("MIDDLE", 40),
                    new LevelThresholdDto("SENIOR", 75),
                ],
                [
                    new LevelTestSectionDto("basics", "Основы", 1.0m, null),
                    new LevelTestSectionDto("web", "Веб", 1.0m, null),
                ],
                FallbackCourseId: null)));

        return quizId;
    }

    /// <summary>Choice верно (basics 1/1 = 100%) + оба open_text отвечены (web pending AI).</summary>
    private static List<SubmitLevelTestAnswerItem> AnsweredAll() =>
    [
        new(_choice, [_choiceCorrect], null),
        new(_open1, null, "Мой ответ про DI"),
        new(_open2, null, "Мой ответ про middleware"),
    ];

    private static string ResultUrl(Guid attemptId) => $"/progress/level-test/attempts/{attemptId}/result";
}
