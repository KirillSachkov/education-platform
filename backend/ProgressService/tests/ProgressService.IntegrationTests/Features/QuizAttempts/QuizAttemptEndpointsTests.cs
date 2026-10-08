using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ContentAccess;
using EducationContentService.Contracts.Quizzes;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Quizzes;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.QuizAttempts;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class QuizAttemptEndpointsTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MULTI_CHOICE = "MULTI_CHOICE";
    private const string OPEN_TEXT = "OPEN_TEXT";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";
    private const string LEVEL_TEST = "LEVEL_TEST";
    private const string ENROLLED = "ENROLLED";
    private const int PASSING_SCORE = 70;

    private static readonly Guid _q1 = Guid.NewGuid();
    private static readonly Guid _q2 = Guid.NewGuid();
    private static readonly Guid _q3 = Guid.NewGuid();
    private static readonly Guid _q1Correct = Guid.NewGuid();
    private static readonly Guid _q1Wrong = Guid.NewGuid();
    private static readonly Guid _q2Correct = Guid.NewGuid();
    private static readonly Guid _q3Correct = Guid.NewGuid();
    private static readonly Guid _q3Wrong = Guid.NewGuid();

    public QuizAttemptEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Submit_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(Guid.NewGuid()),
            new SubmitQuizAttemptRequest([]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMy_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(MyAttemptsUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Submit_TwoOfThreeChoiceCorrect_Returns67NotPassed()
    {
        Guid quizId = SeedThreeSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
                new SubmitQuizAnswerItem(_q3, [_q3Wrong], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);

        Assert.Equal(67, result.ScorePercent);
        Assert.False(result.Passed);
        Assert.Equal(PASSING_SCORE, result.PassingScorePercent);
        Assert.Equal(3, result.Questions.Count);
        Assert.True(result.Questions[0].Correct);
        Assert.True(result.Questions[1].Correct);
        Assert.False(result.Questions[2].Correct);
        Assert.Equal([_q3Correct], result.Questions[2].CorrectOptionIds);
    }

    [Fact]
    public async Task Submit_AllCorrect_ThenFailed_BestStays100LastChanges()
    {
        Guid quizId = SeedThreeSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage passedResponse = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
                new SubmitQuizAnswerItem(_q3, [_q3Correct], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, passedResponse.StatusCode);
        QuizAttemptResultResponse passedAttempt = await ReadWrappedResultAsync<QuizAttemptResultResponse>(passedResponse);
        Assert.Equal(100, passedAttempt.ScorePercent);
        Assert.True(passedAttempt.Passed);

        MyQuizAttemptsResponse afterPassed = await GetMyAttemptsAsync(quizId);
        Assert.Equal(100, afterPassed.Best!.ScorePercent);
        Assert.Equal(100, afterPassed.Last!.ScorePercent);
        Assert.Equal(passedAttempt.AttemptId, afterPassed.Best.AttemptId);
        Assert.Equal(passedAttempt.AttemptId, afterPassed.Last.AttemptId);

        HttpResponseMessage failedResponse = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Wrong], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, failedResponse.StatusCode);
        QuizAttemptResultResponse failedAttempt = await ReadWrappedResultAsync<QuizAttemptResultResponse>(failedResponse);
        Assert.Equal(0, failedAttempt.ScorePercent);
        Assert.False(failedAttempt.Passed);

        MyQuizAttemptsResponse afterFailed = await GetMyAttemptsAsync(quizId);
        Assert.Equal(passedAttempt.AttemptId, afterFailed.Best!.AttemptId);
        Assert.Equal(100, afterFailed.Best.ScorePercent);
        Assert.True(afterFailed.Best.Passed);
        Assert.Equal(failedAttempt.AttemptId, afterFailed.Last!.AttemptId);
        Assert.Equal(0, afterFailed.Last.ScorePercent);
        Assert.False(afterFailed.Last.Passed);
    }

    [Fact]
    public async Task Submit_MultiChoicePartialSelection_IsIncorrect()
    {
        Guid quizId = Guid.NewGuid();
        Guid optionA = Guid.NewGuid();
        Guid optionC = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, MULTI_CHOICE, "Вопрос 1", null, null, [optionA, optionC], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [optionA], null)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);

        Assert.Equal(0, result.ScorePercent);
        Assert.False(result.Passed);
        Assert.False(result.Questions[0].Correct);
        Assert.Equal([optionA], result.Questions[0].SelectedOptionIds);
    }

    [Fact]
    public async Task Submit_MultiChoiceExactSelection_IsCorrect()
    {
        Guid quizId = Guid.NewGuid();
        Guid optionA = Guid.NewGuid();
        Guid optionC = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, MULTI_CHOICE, "Вопрос 1", null, null, [optionA, optionC], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [optionC, optionA], null)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);

        Assert.Equal(100, result.ScorePercent);
        Assert.True(result.Passed);
        Assert.True(result.Questions[0].Correct);
    }

    [Fact]
    public async Task Submit_OpenTextOnlyQuiz_Returns100Passed()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, OPEN_TEXT, "Открытый вопрос 1", null, null, [], "Эталонное объяснение")],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, null, "Мой свободный ответ")]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);

        Assert.Equal(100, result.ScorePercent);
        Assert.True(result.Passed);
        Assert.Null(result.Questions[0].Correct);
        Assert.Equal("Мой свободный ответ", result.Questions[0].TextAnswer);
        Assert.Equal("Эталонное объяснение", result.Questions[0].ReferenceAnswer);
    }

    [Fact]
    public async Task Submit_MixedChoiceCorrectAndOpenText_OpenTextExcludedFromScore()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null),
                new QuizAnswerKeyQuestionDto(_q2, OPEN_TEXT, "Открытый вопрос 2", null, null, [], "Эталон для самопроверки"),
            ],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, null, "Рассуждение студента"),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);

        Assert.Equal(100, result.ScorePercent);
        Assert.True(result.Passed);
        Assert.True(result.Questions[0].Correct);
        Assert.Null(result.Questions[1].Correct);
        Assert.Equal("Эталон для самопроверки", result.Questions[1].ReferenceAnswer);
    }

    // ===== Tier-3 по самому квизу (ST-13 #493) =====

    [Fact]
    public async Task Submit_EnrolledQuiz_DeniedEntitlement_Returns403()
    {
        Guid quizId = SeedThreeSingleChoiceQuiz(accessType: ENROLLED);
        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        int attemptsCount = await ExecuteInDb(async db => await db.QuizAttempts.CountAsync());
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task Submit_EnrolledQuiz_WithGrant_Returns200()
    {
        // GrantAll (default) ≈ у юзера есть plan-grant, покрывающий квиз.
        Guid quizId = SeedThreeSingleChoiceQuiz(accessType: ENROLLED);
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int attemptsCount = await ExecuteInDb(async db => await db.QuizAttempts.CountAsync());
        Assert.Equal(1, attemptsCount);
    }

    [Fact]
    public async Task Submit_PublicQuiz_DeniedEntitlements_StillReturns200()
    {
        // PUBLIC short-circuit: открытый квиз не ходит в entitlement-checker вообще.
        Guid quizId = SeedThreeSingleChoiceQuiz();
        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Submit_LevelTestQuiz_Returns400LevelTestForbidden()
    {
        // У level-test собственный флоу попыток (/level-test) — обычный сабмит запрещён.
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            LEVEL_TEST,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("quiz.attempt.level.test.forbidden", await ReadErrorCodeAsync(response));

        int attemptsCount = await ExecuteInDb(async db => await db.QuizAttempts.CountAsync());
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task Submit_UnknownQuiz_Returns404()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(Guid.NewGuid()),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Submit_EducationContentServiceUnavailable_Returns500()
    {
        EducationContentClient.SetQuizAnswerKeysUnavailable();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(Guid.NewGuid()),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task Submit_DuplicateQuestionIds_Returns400()
    {
        Guid quizId = SeedThreeSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q1, [_q1Wrong], null),
            ]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Submit_UnknownQuestionId_IgnoredInScoreAndNotPersisted()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(Guid.NewGuid(), [Guid.NewGuid()], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(response);
        Assert.Equal(100, result.ScorePercent);
        Assert.Single(result.Questions);

        QuizAttempt stored = await ExecuteInDb(async db => await db.QuizAttempts.SingleAsync());
        Assert.Single(stored.Answers);
        Assert.Equal(_q1, stored.Answers[0].QuestionId);
    }

    [Fact]
    public async Task Submit_AllQuestionIdsStale_Returns409AndNotPersisted()
    {
        // Автор отредактировал квиз → ECS перегенерировал id вопросов. Студент сабмитит
        // со старыми id — НИ ОДИН не совпадает с актуальным ключом → 409, попытка не пишется.
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        // Все questionId из запроса — из старой версии квиза, в ключе их нет.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(Guid.NewGuid(), [Guid.NewGuid()], null),
                new SubmitQuizAnswerItem(Guid.NewGuid(), [Guid.NewGuid()], null),
            ]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("quiz.changed.reload", await ReadErrorCodeAsync(response));

        int attemptsCount = await ExecuteInDb(async db => await db.QuizAttempts.CountAsync());
        Assert.Equal(0, attemptsCount);
    }

    [Fact]
    public async Task Submit_AnswersJsonbRoundtrip_GetMyReturnsSubmitted()
    {
        Guid quizId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(_q1, MULTI_CHOICE, "Вопрос 1", null, null, [_q1Correct, _q1Wrong], null),
                new QuizAnswerKeyQuestionDto(_q2, OPEN_TEXT, "Открытый вопрос 2", null, null, [], "Эталон"),
            ],
            LevelTestConfig: null));

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct, _q1Wrong], null),
                new SubmitQuizAnswerItem(_q2, null, "Текст моего ответа"),
            ]));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        QuizAttempt stored = await ExecuteInDb(async db => await db.QuizAttempts.SingleAsync());
        Assert.Equal(userId, stored.UserId);
        Assert.Equal(quizId, stored.QuizId);
        Assert.Equal(2, stored.Answers.Count);
        Assert.Equal([_q1Correct, _q1Wrong], stored.Answers[0].SelectedOptionIds);
        Assert.Equal("Текст моего ответа", stored.Answers[1].TextAnswer);

        MyQuizAttemptsResponse my = await GetMyAttemptsAsync(quizId);
        QuizAttemptQuestionResultResponse choiceQuestion = my.Last!.Questions.Single(q => q.QuestionId == _q1);
        QuizAttemptQuestionResultResponse openQuestion = my.Last.Questions.Single(q => q.QuestionId == _q2);
        Assert.Equal([_q1Correct, _q1Wrong], choiceQuestion.SelectedOptionIds);
        Assert.Equal("Текст моего ответа", openQuestion.TextAnswer);
        Assert.Equal("Эталон", openQuestion.ReferenceAnswer);
    }

    [Fact]
    public async Task Submit_QuestionWithExplanation_GetMyRevealsExplanation_NullWhenAbsent()
    {
        // #561: пояснение «почему так» доезжает в разбор попытки (GET /attempts/my).
        // Вопрос с заданным Explanation → значение; без → null.
        Guid quizId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        const string explanation = "string и object — ссылочные, int — значимый";
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(
                    _q1, SINGLE_CHOICE, "Вопрос с пояснением", null, null, [_q1Correct], null,
                    Options: null, Explanation: explanation),
                new QuizAnswerKeyQuestionDto(
                    _q2, SINGLE_CHOICE, "Вопрос без пояснения", null, null, [_q2Correct], null),
            ],
            LevelTestConfig: null));

        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
            ]));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        // Разбор сразу в ответе на сабмит.
        QuizAttemptResultResponse result = await ReadWrappedResultAsync<QuizAttemptResultResponse>(submitResponse);
        Assert.Equal(explanation, result.Questions.Single(q => q.QuestionId == _q1).Explanation);
        Assert.Null(result.Questions.Single(q => q.QuestionId == _q2).Explanation);

        // И в GET /attempts/my.
        MyQuizAttemptsResponse my = await GetMyAttemptsAsync(quizId);
        QuizAttemptQuestionResultResponse withExplanation = my.Last!.Questions.Single(q => q.QuestionId == _q1);
        QuizAttemptQuestionResultResponse withoutExplanation = my.Last.Questions.Single(q => q.QuestionId == _q2);
        Assert.Equal(explanation, withExplanation.Explanation);
        Assert.Null(withoutExplanation.Explanation);
    }

    [Fact]
    public async Task GetMy_NoAttempts_ReturnsNulls()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        MyQuizAttemptsResponse my = await GetMyAttemptsAsync(Guid.NewGuid());

        Assert.Null(my.Best);
        Assert.Null(my.Last);
    }

    [Fact]
    public async Task GetMy_DoesNotReturnOtherUsersAttempts()
    {
        Guid quizId = SeedThreeSingleChoiceQuiz();

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        HttpResponseMessage submitResponse = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(_q1, [_q1Correct], null)]));
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);

        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        MyQuizAttemptsResponse my = await GetMyAttemptsAsync(quizId);

        Assert.Null(my.Best);
        Assert.Null(my.Last);
    }

    // ===== Passed-попытка → module_item_progress cascade (ST-13 #493) =====

    [Fact]
    public async Task Submit_PassedAttempt_CascadesQuizModuleItemProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid quizId = SeedThreeSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddQuizModuleContext(quizId, courseId, moduleId, moduleItemsTotal: 1);
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
                new SubmitQuizAnswerItem(_q3, [_q3Correct], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Lazy progress-anchor создан каскадом (enrollment'а заранее не было).
        Guid enrollmentId = await ExecuteInDb(async db =>
            (await db.CourseEnrollments.SingleAsync(x => x.UserId == userId && x.CourseId == courseId)).Id);

        ModuleItemProgress moduleItem = await ExecuteInDb(db =>
            db.ModuleItemProgresses.SingleAsync(x =>
                x.EnrollmentId == enrollmentId
                && x.ModuleId == moduleId
                && x.ReferenceId == quizId
                && x.ItemType == ModuleItemProgressType.QUIZ));
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItem.Status);

        ModuleProgress moduleProgress = await ExecuteInDb(db =>
            db.ModuleProgresses.SingleAsync(x => x.EnrollmentId == enrollmentId && x.ModuleId == moduleId));
        Assert.Equal(1, moduleProgress.ItemsCompleted);
        Assert.Equal(ModuleProgressStatus.COMPLETED, moduleProgress.Status);
    }

    [Fact]
    public async Task Submit_BelowPassingScore_NoModuleItemCascade()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid quizId = SeedThreeSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddQuizModuleContext(quizId, courseId, moduleId, moduleItemsTotal: 1);
        AuthenticateAs(userId, "platform-participant");

        // 2 из 3 = 67% < 70 — попытка сохраняется, но элемент модуля не закрывается.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
                new SubmitQuizAnswerItem(_q3, [_q3Wrong], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int attempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync(x => x.QuizId == quizId));
        Assert.Equal(1, attempts);

        int moduleItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == quizId));
        Assert.Equal(0, moduleItems);
    }

    [Fact]
    public async Task Submit_RepeatedPass_ModuleItemCascadeIsIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid quizId = SeedThreeSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddQuizModuleContext(quizId, courseId, moduleId, moduleItemsTotal: 2);
        AuthenticateAs(userId, "platform-participant");

        SubmitQuizAttemptRequest allCorrect = new(
        [
            new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
            new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
            new SubmitQuizAnswerItem(_q3, [_q3Correct], null),
        ]);

        Assert.Equal(HttpStatusCode.OK,
            (await AppHttpClient.PostAsJsonAsync(AttemptsUrl(quizId), allCorrect)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AppHttpClient.PostAsJsonAsync(AttemptsUrl(quizId), allCorrect)).StatusCode);

        int attempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync(x => x.QuizId == quizId));
        Assert.Equal(2, attempts);

        // Повторный pass — событие поднимается снова, но handler идемпотентен:
        // одна строка module_item_progress, счётчик модуля не раздувается.
        int moduleItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == quizId));
        Assert.Equal(1, moduleItems);

        ModuleProgress moduleProgress = await ExecuteInDb(db =>
            db.ModuleProgresses.SingleAsync(x => x.ModuleId == moduleId));
        Assert.Equal(1, moduleProgress.ItemsCompleted);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, moduleProgress.Status);
    }

    [Fact]
    public async Task Submit_PassedAttempt_WithoutCourseAccess_NoCascadeButAttemptSaved()
    {
        // PUBLIC-квиз размещён в платном курсе: попытка проходит (short-circuit),
        // но прогресс/anchor на курсе без entitlement'а не создаются.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid quizId = SeedThreeSingleChoiceQuiz();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid());
        EducationContentClient.AddQuizModuleContext(quizId, courseId, moduleId, moduleItemsTotal: 1);
        EntitlementChecker.DenyResourceType(ResourceTypes.COURSE);
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            AttemptsUrl(quizId),
            new SubmitQuizAttemptRequest(
            [
                new SubmitQuizAnswerItem(_q1, [_q1Correct], null),
                new SubmitQuizAnswerItem(_q2, [_q2Correct], null),
                new SubmitQuizAnswerItem(_q3, [_q3Correct], null),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        int attempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync(x => x.QuizId == quizId));
        Assert.Equal(1, attempts);

        int enrollments = await ExecuteInDb(db =>
            db.CourseEnrollments.CountAsync(x => x.UserId == userId && x.CourseId == courseId));
        Assert.Equal(0, enrollments);

        int moduleItems = await ExecuteInDb(db =>
            db.ModuleItemProgresses.CountAsync(x => x.ReferenceId == quizId));
        Assert.Equal(0, moduleItems);
    }

    private Guid SeedThreeSingleChoiceQuiz(string accessType = "PUBLIC")
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1Correct], null),
                new QuizAnswerKeyQuestionDto(_q2, SINGLE_CHOICE, "Вопрос 2", null, null, [_q2Correct], null),
                new QuizAnswerKeyQuestionDto(_q3, SINGLE_CHOICE, "Вопрос 3", null, null, [_q3Correct], null),
            ],
            LevelTestConfig: null,
            AccessType: accessType));
        return quizId;
    }

    private async Task<MyQuizAttemptsResponse> GetMyAttemptsAsync(Guid quizId)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(MyAttemptsUrl(quizId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadWrappedResultAsync<MyQuizAttemptsResponse>(response);
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement messages = document.RootElement.GetProperty("error").GetProperty("messages");
        return messages[0].GetProperty("code").GetString()!;
    }

    private static string AttemptsUrl(Guid quizId) => $"/progress/quizzes/{quizId}/attempts";

    private static string MyAttemptsUrl(Guid quizId) => $"/progress/quizzes/{quizId}/attempts/my";
}
