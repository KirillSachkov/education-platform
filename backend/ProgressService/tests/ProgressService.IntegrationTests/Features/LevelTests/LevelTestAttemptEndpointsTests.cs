using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Quizzes;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Features.LevelTests.IntegrationEvents;
using ProgressService.Domain.LevelTests;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.LevelTests;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class LevelTestAttemptEndpointsTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MULTI_CHOICE = "MULTI_CHOICE";
    private const string OPEN_TEXT = "OPEN_TEXT";
    private const string LEVEL_TEST = "LEVEL_TEST";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";

    private const string SUBMIT_URL = "/progress/level-test/attempts";
    private const string CLAIM_URL = "/progress/level-test/attempts/claim";

    // Mixed-квиз: 2 секции, разные сложности, один open_text.
    private static readonly Guid _q1 = Guid.NewGuid(); // basics / JUNIOR (1 балл)
    private static readonly Guid _q2 = Guid.NewGuid(); // basics / MIDDLE (2 балла)
    private static readonly Guid _q3 = Guid.NewGuid(); // web / SENIOR (3 балла)
    private static readonly Guid _q4 = Guid.NewGuid(); // web / MIDDLE / OPEN_TEXT (2 балла)
    private static readonly Guid _q5 = Guid.NewGuid(); // web / без сложности (1 балл)
    private static readonly Guid _q1Correct = Guid.NewGuid();
    private static readonly Guid _q2CorrectA = Guid.NewGuid();
    private static readonly Guid _q2CorrectB = Guid.NewGuid();
    private static readonly Guid _q3Correct = Guid.NewGuid();
    private static readonly Guid _q5Correct = Guid.NewGuid();

    // Полные наборы вариантов choice-вопросов — нужны для снапшота разбора (#561).
    private static readonly Guid _q1Wrong = Guid.NewGuid();
    private static readonly Guid _q2WrongC = Guid.NewGuid();
    private static readonly Guid _q3Wrong = Guid.NewGuid();
    private static readonly Guid _q5Wrong = Guid.NewGuid();

    private const string _q1Explanation = "CLR — это среда выполнения .NET";

    private static readonly Guid _basicsCourseId = Guid.NewGuid();
    private static readonly Guid _webCourseId = Guid.NewGuid();
    private static readonly Guid _fallbackCourseId = Guid.NewGuid();

    public LevelTestAttemptEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task SubmitAnon_ReturnsTeaserOnly_WithoutSectionsAndQuestions()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, Guid.NewGuid().ToString(), MixedAnswers()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string rawJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"sections\"", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"questions\"", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"recommendedCourseId\"", rawJson, StringComparison.OrdinalIgnoreCase);

        LevelTestAttemptTeaserResponse teaser =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(response);
        Assert.NotEqual(Guid.Empty, teaser.AttemptId);
        Assert.Equal(61, teaser.OverallPercent);
        Assert.Equal("MIDDLE", teaser.Level);
        Assert.Equal(5, teaser.TotalQuestions);
        Assert.Equal(4, teaser.AnsweredCount);
        Assert.Equal("QUEUED", teaser.AiGradingStatus);
    }

    [Fact]
    public async Task SubmitAuth_ReturnsFullResult()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(response);

        Assert.Equal(quizId, result.QuizId);
        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(5, result.Questions.Count);
        Assert.Equal(["basics"], result.WeakestSectionKeys);
        Assert.Equal(_basicsCourseId, result.RecommendedCourseId);

        LevelTestAttempt stored = await ExecuteInDb(async db => await db.LevelTestAttempts.SingleAsync());
        Assert.Equal(userId, stored.UserId);
        Assert.Null(stored.AnonymousId);
        Assert.Null(stored.ClaimedAt);
    }

    [Fact]
    public async Task Submit_MixedDifficultiesAndSections_ScoresChoiceOnlyWhilePendingAi_PublishesGradeMessage()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(response);

        // basics: J-вопрос верно (1/1) + M-вопрос частичный выбор → неверно (0/2) → 1/3 = 33%.
        LevelTestSectionScoreResponse basics = result.Sections.Single(s => s.Key == "basics");
        Assert.Equal("Основы", basics.Title);
        Assert.Equal(33, basics.Percent);
        Assert.Equal("JUNIOR", basics.Level);
        Assert.Equal(1m, basics.EarnedPoints);
        Assert.Equal(3m, basics.MaxPoints);

        // web: S-вопрос верно (3/3) + безуровневый неотвеченный (0/1), open_text (2 балла)
        // ИСКЛЮЧЁН из знаменателя пока AI-грейдинг не завершён → 3/4 = 75%.
        LevelTestSectionScoreResponse web = result.Sections.Single(s => s.Key == "web");
        Assert.Equal(75, web.Percent);
        Assert.Equal("SENIOR", web.Level);
        Assert.Equal(3m, web.EarnedPoints);
        Assert.Equal(4m, web.MaxPoints);

        // overall = (33×1 + 75×2) / 3 = 61 → MIDDLE (40 ≤ 61 < 75).
        Assert.Equal(61, result.OverallPercent);
        Assert.Equal("MIDDLE", result.Level);
        Assert.Equal("QUEUED", result.AiGradingStatus);

        // Слабейшая секция (weight>0) — basics → её рекомендованный курс.
        Assert.Equal(_basicsCourseId, result.RecommendedCourseId);

        LevelTestQuestionResultResponse openText = result.Questions.Single(q => q.QuestionId == _q4);
        Assert.True(openText.PendingAi);
        Assert.Null(openText.IsCorrect);
        Assert.Equal(0m, openText.EarnedPoints);
        Assert.Equal(2m, openText.MaxPoints);

        LevelTestQuestionResultResponse unanswered = result.Questions.Single(q => q.QuestionId == _q5);
        Assert.False(unanswered.IsCorrect);
        Assert.Equal(0m, unanswered.EarnedPoints);
        Assert.Equal(1m, unanswered.MaxPoints);

        LevelTestQuestionResultResponse partialMulti = result.Questions.Single(q => q.QuestionId == _q2);
        Assert.False(partialMulti.IsCorrect);
        Assert.False(partialMulti.PendingAi);

        GradeLevelTestAttemptRequested published = NoOpOutboxService.Published
            .OfType<GradeLevelTestAttemptRequested>()
            .Single();
        Assert.Equal(result.AttemptId, published.AttemptId);
    }

    [Fact]
    public async Task SubmitAuth_FullResult_RevealsCorrectAnswersOptionsAndExplanation()
    {
        // #561: полный разбор раскрывает правильные ответы — варианты (id+text),
        // correctOptionIds, выбор пользователя и пояснение «почему так».
        Guid quizId = SeedMixedLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(response);

        // Choice-вопрос _q1 (ответ верный): options заполнены id+непустой text,
        // correctOptionIds непустой, selectedOptionIds отражает выбор, пояснение доезжает.
        LevelTestQuestionResultResponse single = result.Questions.Single(q => q.QuestionId == _q1);
        Assert.Equal(2, single.Options.Count);
        Assert.All(single.Options, o =>
        {
            Assert.NotEqual(Guid.Empty, o.Id);
            Assert.False(string.IsNullOrWhiteSpace(o.Text));
        });
        Assert.Contains(single.Options, o => o.Id == _q1Correct);
        Assert.Contains(single.Options, o => o.Id == _q1Wrong);
        Assert.Equal([_q1Correct], single.CorrectOptionIds);
        Assert.Equal([_q1Correct], single.SelectedOptionIds);
        Assert.True(single.IsCorrect);
        Assert.Equal(_q1Explanation, single.Explanation);

        // Multi-choice _q2: частичный выбор (_q2CorrectA) → selectedOptionIds его несёт,
        // correctOptionIds полный, без пояснения → Explanation == null.
        LevelTestQuestionResultResponse multi = result.Questions.Single(q => q.QuestionId == _q2);
        Assert.Equal(3, multi.Options.Count);
        Assert.Equal(2, multi.CorrectOptionIds.Count);
        Assert.Contains(_q2CorrectA, multi.CorrectOptionIds);
        Assert.Contains(_q2CorrectB, multi.CorrectOptionIds);
        Assert.Equal([_q2CorrectA], multi.SelectedOptionIds);
        Assert.Null(multi.Explanation);

        // Open-text _q4: эталонный ответ раскрывается, варианты пусты, выбор пуст.
        LevelTestQuestionResultResponse openText = result.Questions.Single(q => q.QuestionId == _q4);
        Assert.Empty(openText.Options);
        Assert.Empty(openText.CorrectOptionIds);
        Assert.Empty(openText.SelectedOptionIds);
        Assert.Equal("Эталонное объяснение", openText.ReferenceAnswer);
        Assert.Equal("Рассуждение о DI и middleware", openText.TextAnswer);

        // Неотвеченный choice _q5: варианты и правильные раскрыты, выбор пуст.
        LevelTestQuestionResultResponse unanswered = result.Questions.Single(q => q.QuestionId == _q5);
        Assert.Equal(2, unanswered.Options.Count);
        Assert.Equal([_q5Correct], unanswered.CorrectOptionIds);
        Assert.Empty(unanswered.SelectedOptionIds);
    }

    [Fact]
    public async Task Submit_WithoutOpenTextAnswer_StatusNone_NoGradeMessage()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(
                quizId,
                AnonymousId: null,
                [new SubmitLevelTestAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(response);

        Assert.Equal("NONE", result.AiGradingStatus);
        Assert.Empty(NoOpOutboxService.Published.OfType<GradeLevelTestAttemptRequested>());
    }

    [Fact]
    public async Task Submit_MaterialCheckQuiz_Returns400()
    {
        Guid quizId = SeedMixedLevelTestQuiz(purpose: MATERIAL_CHECK);
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        int attemptsCount = await ExecuteInDb(async db => await db.LevelTestAttempts.CountAsync());
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task SubmitAnon_WithoutAnonymousId_Returns400()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_UnknownQuiz_Returns404()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(Guid.NewGuid(), AnonymousId: null, MixedAnswers()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Claim_BindsAnonymousAttempts_ThenOwnerSeesFullResult()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        string anonymousId = Guid.NewGuid().ToString();

        RemoveAuthentication();
        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, anonymousId, MixedAnswers()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptTeaserResponse teaser =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(submitResponse);

        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage claimResponse = await AppHttpClient.PostAsJsonAsync(
            CLAIM_URL,
            new ClaimLevelTestAttemptsRequest(anonymousId));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        ClaimLevelTestAttemptsResponse claim =
            await ReadWrappedResultAsync<ClaimLevelTestAttemptsResponse>(claimResponse);

        Assert.Equal(1, claim.ClaimedCount);
        Assert.Equal(teaser.AttemptId, claim.LatestAttemptId);

        LevelTestAttempt stored = await ExecuteInDb(async db => await db.LevelTestAttempts.SingleAsync());
        Assert.Equal(userId, stored.UserId);
        Assert.NotNull(stored.ClaimedAt);

        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(teaser.AttemptId));
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(resultResponse);

        Assert.Equal(2, result.Sections.Count);
        Assert.Equal(5, result.Questions.Count);
        Assert.Equal(61, result.OverallPercent);
    }

    [Fact]
    public async Task Claim_NoMatchingAttempts_ReturnsZero()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            CLAIM_URL,
            new ClaimLevelTestAttemptsRequest(Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        ClaimLevelTestAttemptsResponse claim =
            await ReadWrappedResultAsync<ClaimLevelTestAttemptsResponse>(response);

        Assert.Equal(0, claim.ClaimedCount);
        Assert.Null(claim.LatestAttemptId);
    }

    [Fact]
    public async Task GetResult_ClaimedAttempt_ByAnotherUser_Returns403_Anonymous401()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, AnonymousId: null, MixedAnswers()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptResultResponse owned =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(submitResponse);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        HttpResponseMessage otherUserResponse = await AppHttpClient.GetAsync(ResultUrl(owned.AttemptId));
        Assert.Equal(HttpStatusCode.Forbidden, otherUserResponse.StatusCode);

        RemoveAuthentication();
        HttpResponseMessage anonymousResponse = await AppHttpClient.GetAsync(ResultUrl(owned.AttemptId));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
    }

    [Fact]
    public async Task GetResult_UnclaimedAttempt_ReturnsTeaser()
    {
        Guid quizId = SeedMixedLevelTestQuiz();
        RemoveAuthentication();

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, Guid.NewGuid().ToString(), MixedAnswers()));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        LevelTestAttemptTeaserResponse submitted =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(submitResponse);

        HttpResponseMessage resultResponse = await AppHttpClient.GetAsync(ResultUrl(submitted.AttemptId));
        Assert.Equal(HttpStatusCode.OK, resultResponse.StatusCode);

        string rawJson = await resultResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"sections\"", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"questions\"", rawJson, StringComparison.OrdinalIgnoreCase);

        LevelTestAttemptTeaserResponse teaser =
            await ReadWrappedResultAsync<LevelTestAttemptTeaserResponse>(resultResponse);
        Assert.Equal(submitted.AttemptId, teaser.AttemptId);
        Assert.Equal(61, teaser.OverallPercent);
        Assert.Equal("MIDDLE", teaser.Level);
    }

    [Fact]
    public async Task GetResult_UnknownAttempt_Returns404()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(ResultUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Submit_LevelThresholdBoundary_45IsMiddle()
    {
        // Один JUNIOR(1) + два MIDDLE(2) + два SENIOR(3) = 11 баллов max; верно MIDDLE+SENIOR
        // → 5/11 = 45.45 → 45 → ровно порог MIDDLE (MinPercent 45 ≤ 45).
        Guid quizId = Guid.NewGuid();
        Guid junior = Guid.NewGuid();
        Guid middleA = Guid.NewGuid();
        Guid middleB = Guid.NewGuid();
        Guid seniorA = Guid.NewGuid();
        Guid seniorB = Guid.NewGuid();
        Guid juniorCorrect = Guid.NewGuid();
        Guid middleACorrect = Guid.NewGuid();
        Guid middleBCorrect = Guid.NewGuid();
        Guid seniorACorrect = Guid.NewGuid();
        Guid seniorBCorrect = Guid.NewGuid();

        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            LEVEL_TEST,
            PassingScorePercent: 0,
            [
                new QuizAnswerKeyQuestionDto(junior, SINGLE_CHOICE, "Вопрос J", null, "JUNIOR", [juniorCorrect], null),
                new QuizAnswerKeyQuestionDto(middleA, SINGLE_CHOICE, "Вопрос M1", null, "MIDDLE", [middleACorrect], null),
                new QuizAnswerKeyQuestionDto(middleB, SINGLE_CHOICE, "Вопрос M2", null, "MIDDLE", [middleBCorrect], null),
                new QuizAnswerKeyQuestionDto(seniorA, SINGLE_CHOICE, "Вопрос S1", null, "SENIOR", [seniorACorrect], null),
                new QuizAnswerKeyQuestionDto(seniorB, SINGLE_CHOICE, "Вопрос S2", null, "SENIOR", [seniorBCorrect], null),
            ],
            new LevelTestConfigDto(
                [
                    new LevelThresholdDto("JUNIOR", 0),
                    new LevelThresholdDto("MIDDLE", 45),
                    new LevelThresholdDto("SENIOR", 75),
                ],
                Sections: [],
                FallbackCourseId: null)));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(
                quizId,
                AnonymousId: null,
                [
                    new SubmitLevelTestAnswerItem(middleA, [middleACorrect], null),
                    new SubmitLevelTestAnswerItem(seniorA, [seniorACorrect], null),
                ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestAttemptResultResponse result =
            await ReadWrappedResultAsync<LevelTestAttemptResultResponse>(response);

        Assert.Equal(45, result.OverallPercent);
        Assert.Equal("MIDDLE", result.Level);
        Assert.Equal("NONE", result.AiGradingStatus);

        // Вопросы без section падают в "general" с весом 1; fallback-курса нет → null.
        LevelTestSectionScoreResponse general = result.Sections.Single();
        Assert.Equal("general", general.Key);
        Assert.Equal(45, general.Percent);
        Assert.Null(result.RecommendedCourseId);
    }

    private Guid SeedMixedLevelTestQuiz(string purpose = LEVEL_TEST)
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            purpose,
            PassingScorePercent: 0,
            [
                new QuizAnswerKeyQuestionDto(
                    _q1, SINGLE_CHOICE, "Что такое CLR?", "basics", "JUNIOR", [_q1Correct], null,
                    [new QuizOptionDto(_q1Correct, "Среда выполнения"), new QuizOptionDto(_q1Wrong, "Компилятор")],
                    _q1Explanation),
                new QuizAnswerKeyQuestionDto(
                    _q2, MULTI_CHOICE, "Какие типы ссылочные?", "basics", "MIDDLE", [_q2CorrectA, _q2CorrectB], null,
                    [
                        new QuizOptionDto(_q2CorrectA, "string"),
                        new QuizOptionDto(_q2CorrectB, "object"),
                        new QuizOptionDto(_q2WrongC, "int"),
                    ]),
                new QuizAnswerKeyQuestionDto(
                    _q3, SINGLE_CHOICE, "Что такое middleware?", "web", "SENIOR", [_q3Correct], null,
                    [new QuizOptionDto(_q3Correct, "Конвейер обработки"), new QuizOptionDto(_q3Wrong, "База данных")]),
                new QuizAnswerKeyQuestionDto(
                    _q4, OPEN_TEXT, "Объясните DI и middleware", "web", "MIDDLE", [], "Эталонное объяснение"),
                new QuizAnswerKeyQuestionDto(
                    _q5, SINGLE_CHOICE, "Что такое REST?", "web", null, [_q5Correct], null,
                    [new QuizOptionDto(_q5Correct, "Архитектурный стиль"), new QuizOptionDto(_q5Wrong, "Язык")]),
            ],
            new LevelTestConfigDto(
                [
                    new LevelThresholdDto("JUNIOR", 0),
                    new LevelThresholdDto("MIDDLE", 40),
                    new LevelThresholdDto("SENIOR", 75),
                ],
                [
                    new LevelTestSectionDto("basics", "Основы", 1.0m, _basicsCourseId),
                    new LevelTestSectionDto("web", "Веб", 2.0m, _webCourseId),
                ],
                _fallbackCourseId)));

        return quizId;
    }

    /// <summary>
    ///     Ответы для mixed-квиза: basics 1/3 (33%), web choice-only 3/4 (75%),
    ///     overall (33×1 + 75×2)/3 = 61 → MIDDLE; open_text отвечен → QUEUED.
    /// </summary>
    private static List<SubmitLevelTestAnswerItem> MixedAnswers() =>
    [
        new(_q1, [_q1Correct], null),
        new(_q2, [_q2CorrectA], null), // частичный выбор MULTI_CHOICE → неверно
        new(_q3, [_q3Correct], null),
        new(_q4, null, "Рассуждение о DI и middleware"),
        // _q5 — без ответа: 0 заработанных, в max входит
    ];

    private static string ResultUrl(Guid attemptId) => $"/progress/level-test/attempts/{attemptId}/result";
}
