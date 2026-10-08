using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EducationContentService.Contracts.Quizzes;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.QuizAttempts;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CheckQuizQuestionTests : ProgressServiceTestsBase
{
    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MULTI_CHOICE = "MULTI_CHOICE";
    private const string EXACT_TEXT = "EXACT_TEXT";
    private const string OPEN_TEXT = "OPEN_TEXT";
    private const string MATERIAL_CHECK = "MATERIAL_CHECK";
    private const string LEVEL_TEST = "LEVEL_TEST";
    private const string ENROLLED = "ENROLLED";
    private const int PASSING_SCORE = 70;

    private static readonly Guid _q1 = Guid.NewGuid();
    private static readonly Guid _q1OptionA = Guid.NewGuid();
    private static readonly Guid _q1OptionB = Guid.NewGuid();

    public CheckQuizQuestionTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Check_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await PostCheckAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Check_CorrectSingleChoice_ReturnsCorrectTrueWithKey()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckQuizQuestionResponse result = await ReadWrappedResultAsync<CheckQuizQuestionResponse>(response);

        Assert.Equal(_q1, result.QuestionId);
        Assert.Equal(SINGLE_CHOICE, result.Type);
        Assert.True(result.Correct);
        Assert.Equal([_q1OptionA], result.CorrectOptionIds);
        Assert.Equal(2, result.Options.Count);
        Assert.Contains(result.Options, o => o.Id == _q1OptionA && o.Text == "Верный");

        // Чек ничего не сохраняет.
        int attempts = await ExecuteInDb(db => db.QuizAttempts.CountAsync());
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Check_WrongSingleChoice_ReturnsCorrectFalse()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionB], null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckQuizQuestionResponse result = await ReadWrappedResultAsync<CheckQuizQuestionResponse>(response);

        Assert.False(result.Correct);
        Assert.Equal([_q1OptionA], result.CorrectOptionIds);
    }

    [Fact]
    public async Task Check_MultiChoicePartialSelection_IsIncorrect()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(
                    _q1, MULTI_CHOICE, "Вопрос 1", null, null,
                    [_q1OptionA, _q1OptionB], null,
                    [new QuizOptionDto(_q1OptionA, "A"), new QuizOptionDto(_q1OptionB, "B")]),
            ],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckQuizQuestionResponse result = await ReadWrappedResultAsync<CheckQuizQuestionResponse>(response);
        Assert.False(result.Correct);
    }

    [Fact]
    public async Task Check_ExactText_NormalizedMatch_IsCorrect()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, EXACT_TEXT, "Сколько будет 1+4+9?", null, null, [], "149")],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest(null, "1, 4, 9"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckQuizQuestionResponse result = await ReadWrappedResultAsync<CheckQuizQuestionResponse>(response);
        Assert.True(result.Correct);
        Assert.Equal("149", result.ReferenceAnswer);
    }

    [Fact]
    public async Task Check_OpenText_ReturnsCorrectNullWithReferenceAnswer()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, OPEN_TEXT, "Объясни GC", null, null, [], "Эталонное объяснение")],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest(null, "Мой свободный ответ"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckQuizQuestionResponse result = await ReadWrappedResultAsync<CheckQuizQuestionResponse>(response);
        Assert.Null(result.Correct);
        Assert.Equal("Эталонное объяснение", result.ReferenceAnswer);
    }

    [Fact]
    public async Task Check_LevelTestQuiz_Returns400Forbidden()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            LEVEL_TEST,
            PASSING_SCORE,
            [new QuizAnswerKeyQuestionDto(_q1, SINGLE_CHOICE, "Вопрос 1", null, null, [_q1OptionA], null)],
            LevelTestConfig: null));

        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("quiz.check.level.test.forbidden", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Check_EnrolledQuiz_DeniedEntitlement_Returns403()
    {
        Guid quizId = SeedSingleChoiceQuiz(accessType: ENROLLED);
        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Check_PublicQuiz_DeniedEntitlement_StillReturns200()
    {
        // PUBLIC short-circuit: открытый квиз не ходит в entitlement-checker вообще.
        Guid quizId = SeedSingleChoiceQuiz();
        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Check_UnknownQuiz_Returns404()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            Guid.NewGuid(),
            _q1,
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Check_UnknownQuestionId_Returns404QuestionNotFound()
    {
        Guid quizId = SeedSingleChoiceQuiz();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostCheckAsync(
            quizId,
            Guid.NewGuid(),
            new CheckQuizQuestionRequest([_q1OptionA], null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("quiz.question.not.found", await ReadErrorCodeAsync(response));
    }

    private Guid SeedSingleChoiceQuiz(string accessType = "PUBLIC")
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            MATERIAL_CHECK,
            PASSING_SCORE,
            [
                new QuizAnswerKeyQuestionDto(
                    _q1, SINGLE_CHOICE, "Вопрос 1", null, null,
                    [_q1OptionA], null,
                    [new QuizOptionDto(_q1OptionA, "Верный"), new QuizOptionDto(_q1OptionB, "Неверный")]),
            ],
            LevelTestConfig: null,
            AccessType: accessType));
        return quizId;
    }

    private Task<HttpResponseMessage> PostCheckAsync(Guid quizId, Guid questionId, CheckQuizQuestionRequest request) =>
        AppHttpClient.PostAsJsonAsync($"/progress/quizzes/{quizId}/questions/{questionId}/check", request);

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement messages = document.RootElement.GetProperty("error").GetProperty("messages");
        return messages[0].GetProperty("code").GetString()!;
    }
}
