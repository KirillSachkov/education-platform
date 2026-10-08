using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharedKernel;
using Shared.AI;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Features.Sessions.Grading;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Voice-answer-submit endpoint (#585, NEW design — server transcribes, student does not edit) +
///     deferred AI grading of a mock-interview: the start is END_OF_SESSION (answered open item hidden
///     until complete); the voice endpoint transcribes the audio and records the transcript as the
///     OPEN_TEXT answer (PENDING — identical to a typed CheckAnswer); Complete with an answered
///     OPEN_TEXT marks GradingStatus=PENDING; running the grading service fills the open
///     verdict/score/feedback and the session-level overall feedback, flipping the status to GRADED.
///     AI clients are mocked.
/// </summary>
public sealed class MockAiGradingTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    // --- (a) voice-answer-submit endpoint ---

    [Fact]
    public async Task SubmitVoiceAnswer_records_transcript_as_pending_open_answer()
    {
        StubTranscription("Поколенческий GC: поколения 0, 1, 2; выжившие продвигаются.");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "audio/webm");

        // 200 + PENDING (END_OF_SESSION mock-interview hides the key just like a typed CheckAnswer).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckAnswerResponse check = await ReadResultAsync<CheckAnswerResponse>(response);
        Assert.Equal(open.Id, check.ItemId);
        Assert.Equal("PENDING", check.Verdict);
        Assert.Null(check.ScorePercent);
        Assert.Null(check.ReferenceAnswer);

        // The transcript is now recorded as the item's answer (AnswerRaw = the canned STT text).
        SessionDto fetched = await GetSessionAsync(session.Id);
        SessionItemDto answered = ItemFor(fetched, q.OpenQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Equal("Поколенческий GC: поколения 0, 1, 2; выжившие продвигаются.", answered.AnswerRaw);
    }

    [Fact]
    public async Task SubmitVoiceAnswer_for_session_of_another_user_returns_404()
    {
        StubTranscription("распознанный текст");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        // Switch to a different (non-admin) user — the session is scoped by UserId.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "audio/webm");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitVoiceAnswer_on_non_open_text_item_returns_400()
    {
        StubTranscription("распознанный текст");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);

        // The SINGLE_CHOICE item is auto-graded — a voice answer is rejected.
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);
        HttpResponseMessage response = await PostVoiceAsync(session.Id, single.Id, AudioBytes(2048), "audio/webm");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.answer.not_open_text", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task SubmitVoiceAnswer_with_non_audio_content_type_returns_400()
    {
        StubTranscription("распознанный текст");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "text/plain");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.transcribe.invalid_audio", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task SubmitVoiceAnswer_with_oversized_audio_returns_400()
    {
        StubTranscription("распознанный текст");
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        // 25 MB + 1 byte → over the Whisper limit.
        byte[] oversized = AudioBytes((25 * 1024 * 1024) + 1);
        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, oversized, "audio/webm");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.transcribe.invalid_audio", await ReadErrorCodeAsync(response));
    }

    // --- (b) mock-interview start is END_OF_SESSION (deferred reveal) ---

    [Fact]
    public async Task StartMockInterview_is_END_OF_SESSION_and_hides_answered_open_item_until_complete()
    {
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);

        SessionDto session = await StartMockInterviewAsync(mockId);
        Assert.Equal("MOCK", session.Mode);
        Assert.Equal("END_OF_SESSION", session.RevealPolicy);
        Assert.Equal("NOT_REQUIRED", session.GradingStatus);

        // Answer the OPEN item — END_OF_SESSION keeps the verdict/score/key hidden (PENDING) on check.
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        CheckAnswerResponse check = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Мой устный ответ про поколения GC."));
        Assert.Equal("PENDING", check.Verdict);
        Assert.Null(check.ScorePercent);
        Assert.Null(check.ReferenceAnswer);

        // GET while IN_PROGRESS — the answered open item hides verdict + score + reference answer
        // (END_OF_SESSION reveal gate: a graded-at-end session leaks nothing until Complete).
        SessionDto fetched = await GetSessionAsync(session.Id);
        SessionItemDto answered = ItemFor(fetched, q.OpenQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Null(answered.Verdict);
        Assert.Null(answered.ScorePercent);
        Assert.Null(answered.ReferenceAnswer);
        Assert.Null(answered.Feedback);

        // Raw body must not carry the open reference answer while still in progress.
        HttpResponseMessage raw = await Client.GetAsync($"/trainer/sessions/{session.Id}");
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, await ReadRawAsync(raw), StringComparison.Ordinal);
    }

    // --- (c) deferred grading end-to-end ---

    [Fact]
    public async Task Complete_mock_with_open_answer_marks_PENDING_then_grading_produces_GRADED_with_feedback()
    {
        StubOpenAnswerGrade("PARTIAL", 60, "Неплохо, но не хватило про большие объекты (LOH).");
        StubOverallFeedback(
            "В целом достойно: основы GC раскрыты, но есть пробелы.",
            ["Управление памятью"],
            ["Базовое понимание поколений"]);

        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);

        // Answer the OPEN item only. The three unanswered deterministic items remain zeroes in the
        // denominator after deferred grading: (60 + 0 + 0 + 0) / 4 = 15.
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        await CheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, "Устный ответ про поколенческий GC."));

        // Complete → PENDING (an answered OPEN_TEXT item triggers deferred AI grading).
        HttpResponseMessage completeResponse =
            await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        SessionSummaryDto summary = await ReadResultAsync<SessionSummaryDto>(completeResponse);
        Assert.Equal("COMPLETED", summary.Status);
        Assert.Equal("PENDING", summary.GradingStatus);

        // Drive the grader directly (don't rely on the background timing).
        await GradeSessionAsync(session.Id);

        // GET shows the open item graded + the session-level AI feedback + GRADED status.
        SessionDto graded = await GetSessionAsync(session.Id);
        Assert.Equal("GRADED", graded.GradingStatus);
        Assert.Equal(15, graded.ScorePercent);

        SessionItemDto gradedOpen = ItemFor(graded, q.OpenQuestionId);
        Assert.Equal("PARTIAL", gradedOpen.Verdict);
        Assert.Equal(60, gradedOpen.ScorePercent);
        Assert.Equal("Неплохо, но не хватило про большие объекты (LOH).", gradedOpen.Feedback);

        Assert.Equal("В целом достойно: основы GC раскрыты, но есть пробелы.", graded.AiOverallFeedback);
        Assert.Equal(["Управление памятью"], graded.AiWeakTopics);
        Assert.Equal(["Базовое понимание поколений"], graded.AiStrengths);
    }

    [Fact]
    public async Task Grading_marks_empty_open_answer_INCORRECT_without_calling_llm()
    {
        StubOpenAnswerGrade("CORRECT", 100, "не должно вызываться"); // empty-answer path skips the LLM
        StubOverallFeedback("итог", [], []);

        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);

        // Answer the OPEN item with whitespace only → AnswerRaw is null → INCORRECT/0, no LLM call.
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        await CheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, "   "));

        await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);
        await GradeSessionAsync(session.Id);

        SessionDto graded = await GetSessionAsync(session.Id);
        Assert.Equal("GRADED", graded.GradingStatus);
        SessionItemDto gradedOpen = ItemFor(graded, q.OpenQuestionId);
        Assert.Equal("INCORRECT", gradedOpen.Verdict);
        Assert.Equal(0, gradedOpen.ScorePercent);
        Assert.Equal("Ответа нет", gradedOpen.Feedback);
    }

    [Fact]
    public async Task Grading_marks_session_FAILED_when_an_answer_could_not_be_graded()
    {
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        await CheckAsync(session.Id, open.Id, new CheckAnswerRequest(null, "Ответ для недоступного AI."));
        await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);
        Factory.OpenAnswerGrader.FailNext = true;

        await GradeSessionAsync(session.Id);

        SessionDto failed = await GetSessionAsync(session.Id);
        Assert.Equal("FAILED", failed.GradingStatus);
        Assert.Null(ItemFor(failed, q.OpenQuestionId).ScorePercent);
        Assert.Null(failed.AiOverallFeedback);
    }

    [Fact]
    public async Task Grading_does_not_score_unanswered_open_items()
    {
        StubOpenAnswerGrade("PARTIAL", 60, "Частичный ответ.");
        StubOverallFeedback("итог", [], []);
        AuthenticateAsAdmin();
        Guid topicId = await SeedPublishedTwoOpenTopicAsync();
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        SessionItemDto[] openItems = session.Items
            .Where(i => string.Equals(i.QuestionType, "OPEN_TEXT", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, openItems.Length);

        await CheckAsync(
            session.Id,
            openItems[0].Id,
            new CheckAnswerRequest(null, "Ответ только на один вопрос."));
        await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);

        await GradeSessionAsync(session.Id);

        SessionDto graded = await GetSessionAsync(session.Id);
        Assert.Equal("GRADED", graded.GradingStatus);
        Assert.Equal(60, graded.ScorePercent);
        Assert.Equal(60, graded.Items.Single(i => i.Id == openItems[0].Id).ScorePercent);
        SessionItemDto unanswered = graded.Items.Single(i => i.Id == openItems[1].Id);
        Assert.False(unanswered.IsAnswered);
        Assert.Null(unanswered.ScorePercent);
        Assert.Equal(1, Factory.OpenAnswerGrader.CallCount);
    }

    // --- AI stubs ---

    private void StubTranscription(string text) =>
        AiTranscription.TranscribeAsync(Arg.Any<AiTranscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiTranscriptionResult, Error>(
                new AiTranscriptionResult("test", "whisper-1", "ru", text, [], 30)));

    /// <summary>
    ///     Configures the fake open-answer grader (#568 W2) used by both the mock background path and
    ///     the inline non-mock path. The per-answer verdict/score/feedback come from the fake; the
    ///     aggregate interview feedback is stubbed separately on <see cref="StubOverallFeedback"/>
    ///     (still via the LLM client). An empty answer is INCORRECT/0 inside the fake regardless.
    /// </summary>
    private void StubOpenAnswerGrade(string verdict, int score, string feedback)
    {
        Factory.OpenAnswerGrader.Verdict = Enum.Parse<TrainerService.Domain.AnswerVerdict>(verdict);
        Factory.OpenAnswerGrader.ScorePercent = score;
        Factory.OpenAnswerGrader.Feedback = feedback;
    }

    private void StubOverallFeedback(string overallFeedback, string[] weakTopics, string[] strengths)
    {
        string json = JsonSerializer.Serialize(new { overallFeedback, weakTopics, strengths });
        AiClient.GenerateAsync<JsonElement>(
                Arg.Is<AiGenerationRequest>(r => r.JsonSchema!.Name == "trainer_mock_feedback"),
                Arg.Any<CancellationToken>())
            .Returns(GenResult(json));
    }

    private static Result<AiGenerationResult<JsonElement>, Error> GenResult(string json)
    {
        JsonElement element = JsonDocument.Parse(json).RootElement.Clone();
        return Result.Success<AiGenerationResult<JsonElement>, Error>(
            new AiGenerationResult<JsonElement>(element, "test", "gpt-4.1-mini", null, AiFinishReason.Stop));
    }

    // --- helpers ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private static byte[] AudioBytes(int length) => Encoding.UTF8.GetBytes(new string('a', length));

    private async Task<HttpResponseMessage> PostVoiceAsync(
        Guid sessionId, Guid itemId, byte[] bytes, string contentType)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", "answer.webm");
        return await Client.PostAsync($"/trainer/sessions/{sessionId}/answers/{itemId}/voice", form);
    }

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<SessionDto> GetSessionAsync(Guid sessionId)
    {
        HttpResponseMessage response = await Client.GetAsync($"/trainer/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    /// <summary>Drives the AI grader directly within a DI scope (deterministic — no background timing).</summary>
    private async Task GradeSessionAsync(Guid sessionId)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        MockAnswerGradingService grader =
            scope.ServiceProvider.GetRequiredService<MockAnswerGradingService>();
        await grader.GradeSessionAsync(sessionId, CancellationToken.None);
    }

    /// <summary>Seeds a published topic with a FREE bank (4 local questions incl. OPEN_TEXT). Caller must be admin.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "mock-ai-topic", "Тема для мок-собеса", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, questions);
    }

    private async Task<Guid> SeedPublishedTwoOpenTopicAsync()
    {
        Guid trackId = await CreateTrackAsync("mock-two-open", "Mock two open", "CSHARP");
        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(
                trackId,
                "mock-two-open-topic",
                "Два открытых вопроса",
                "Runtime",
                null,
                null,
                null,
                null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;
        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        await TrainerQuestionFixtures.SeedOpenTextOnlyAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return topicId;
    }

    private async Task<Guid> CreateMockInterviewAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-interviews",
            new CreateMockInterviewRequest("net-junior", "Найм: .NET junior", null, [topicId], null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<MockInterviewIdResponse>(response)).Id;
    }

    private async Task<SessionDto> StartMockInterviewAsync(Guid mockInterviewId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/mock-interviews/{mockInterviewId}/sessions",
            new StartMockInterviewRequest(QuestionCount: null, TimeLimitSeconds: 1800));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }
}
