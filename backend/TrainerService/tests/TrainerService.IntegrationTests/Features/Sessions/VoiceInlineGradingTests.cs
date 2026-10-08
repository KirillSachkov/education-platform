using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel;
using Shared.AI;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Голосовой OPEN_TEXT-ответ в НЕ-мок сессии (#585): транскрипт грейдится AI <b>инлайн</b> ровно
///     как печатный <c>CheckAnswer</c> — вердикт + балл + фидбэк, mastery + study-state, раскрытие под
///     PER_QUESTION, а распознанный текст возвращается как «Твой ответ» (<c>AnswerText</c>), чтобы
///     разбор не был пустым. Регрессия: раньше голос писался PENDING («Самопроверка») и текст ответа
///     терялся на клиенте. MOCK-сессия по-прежнему НЕ грейдит голос инлайн (отложенный фоновый грейдер).
/// </summary>
public sealed class VoiceInlineGradingTests(IntegrationTestsWebFactory factory)
    : TrainerServiceTestsBase(factory)
{
    private const string Transcript = "Поколенческий GC: поколения 0, 1, 2; выжившие продвигаются.";

    // --- (a) LEARN voice answer is AI-graded inline + transcript surfaced + mastery + study-state ---

    [Fact]
    public async Task VoiceAnswer_in_LEARN_is_AI_graded_inline_and_surfaces_transcript()
    {
        StubTranscription(Transcript);
        Factory.OpenAnswerGrader.Feedback = "Раскрыта суть поколенческого GC.";

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await PostVoiceResultAsync(session.Id, open.Id);

        // The AI verdict/score/feedback are revealed immediately (PER_QUESTION), and «Твой ответ»
        // carries the recognized speech so the review is not empty.
        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(100, result.ScorePercent);
        Assert.Equal("Раскрыта суть поколенческого GC.", result.Feedback);
        Assert.Equal(Transcript, result.AnswerText);
        Assert.Equal(TrainerQuestionFixtures.OpenReference, result.ReferenceAnswer);
        Assert.Equal(1, Factory.OpenAnswerGrader.CallCount);

        // Mastery EWMA updated (first answer = 100) and study-state KNOWN — voice now feeds «изучено».
        TrainerProgressDto progress = await GetProgressAsync();
        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(100, topic.MasteryPercent);
        Assert.Equal(1, topic.AnswersCount);
        Assert.Equal(1, topic.StudiedCount);

        // The graded answer (incl. the transcript) surfaces in the session review.
        SessionDto fetched = await GetSessionAsync(session.Id);
        SessionItemDto answered = ItemFor(fetched, q.OpenQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Equal("CORRECT", answered.Verdict);
        Assert.Equal(100, answered.ScorePercent);
        Assert.Equal(Transcript, answered.AnswerRaw);
        Assert.Equal("Раскрыта суть поколенческого GC.", answered.Feedback);

        // Non-MOCK voice bills BOTH the transcription AND the inline open-answer grade (#614 C1).
        IReadOnlyList<AiUsageOperation> ops = await GetUsageOpsAsync(CurrentUserId);
        Assert.Contains(AiUsageOperation.TRANSCRIPTION, ops);
        Assert.Contains(AiUsageOperation.OPEN_ANSWER_GRADE, ops);
    }

    [Fact]
    public async Task VoiceAnswer_in_LEARN_graded_wrong_records_a_mistake()
    {
        StubTranscription("Что-то совсем не по теме.");
        Factory.OpenAnswerGrader.Verdict = TrainerService.Domain.AnswerVerdict.INCORRECT;
        Factory.OpenAnswerGrader.ScorePercent = 0;
        Factory.OpenAnswerGrader.Feedback = "Не раскрыта суть.";

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await PostVoiceResultAsync(session.Id, open.Id);
        Assert.Equal("INCORRECT", result.Verdict);
        Assert.Equal(0, result.ScorePercent);
        Assert.Equal("Что-то совсем не по теме.", result.AnswerText);

        // A wrong open answer registers a WRONG study-state → surfaces in «Мои ошибки».
        IReadOnlyList<MistakeItemDto> mistakes = await GetMistakesAsync();
        MistakeItemDto mistake = Assert.Single(mistakes);
        Assert.Equal(q.OpenQuestionId, mistake.QuestionId);
        Assert.Equal(topicId, mistake.TopicId);
    }

    // --- (b) fallback: grader fails → PENDING, transcript STILL surfaced, no mastery/study-state ---

    [Fact]
    public async Task VoiceAnswer_in_LEARN_falls_back_to_PENDING_but_still_surfaces_transcript()
    {
        StubTranscription(Transcript);
        Factory.OpenAnswerGrader.FailNext = true; // AI grader returns a failure Result

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await PostVoiceAsync(session.Id, open.Id, AudioBytes(2048), "audio/webm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // an AI outage must not 500 the voice submit
        CheckAnswerResponse result = await ReadResultAsync<CheckAnswerResponse>(response);

        Assert.Equal("PENDING", result.Verdict);
        Assert.Null(result.ScorePercent);
        // Even when the AI grade is unavailable, the recognized speech is still shown — «Твой ответ» is
        // never empty as long as transcription succeeded (graceful self-check fallback).
        Assert.Equal(Transcript, result.AnswerText);

        // PENDING (null score) → mastery untouched + no mistake recorded (the answer can be retried).
        TrainerProgressDto progress = await GetProgressAsync();
        Assert.DoesNotContain(progress.Mastery, m => m.TopicId == topicId && m.AnswersCount > 0);
        Assert.Empty(await GetMistakesAsync());
    }

    // --- (c) MOCK voice answer is NOT graded inline (deferred to the background grader) ---

    [Fact]
    public async Task VoiceAnswer_in_MOCK_is_not_graded_inline()
    {
        StubTranscription(Transcript);
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("voice-mock-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        Assert.Equal("MOCK", session.Mode);

        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        CheckAnswerResponse result = await PostVoiceResultAsync(session.Id, open.Id);

        // MOCK: recorded PENDING (END_OF_SESSION hides the key), the inline grader is never called.
        Assert.Equal("PENDING", result.Verdict);
        Assert.Null(result.ScorePercent);
        Assert.Null(result.Feedback);
        Assert.Equal(0, Factory.OpenAnswerGrader.CallCount);

        // The transcript is still persisted as the answer (the background grader scores it after Complete).
        SessionDto fetched = await GetSessionAsync(session.Id);
        SessionItemDto answered = ItemFor(fetched, q.OpenQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Equal(Transcript, answered.AnswerRaw);
    }

    // --- helpers ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

    private async Task<CheckAnswerResponse> PostVoiceResultAsync(Guid sessionId, Guid itemId)
    {
        HttpResponseMessage response = await PostVoiceAsync(sessionId, itemId, AudioBytes(2048), "audio/webm");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }

    private async Task<HttpResponseMessage> PostVoiceAsync(Guid sessionId, Guid itemId, byte[] bytes, string contentType)
    {
        using MultipartFormDataContent form = new();
        ByteArrayContent file = new(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(file, "audio", "answer.webm");
        return await Client.PostAsync($"/trainer/sessions/{sessionId}/answers/{itemId}/voice", form);
    }

    private static byte[] AudioBytes(int length) => Encoding.UTF8.GetBytes(new string('a', length));

    private void StubTranscription(string text) =>
        AiTranscription.TranscribeAsync(Arg.Any<AiTranscriptionRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success<AiTranscriptionResult, Error>(
                new AiTranscriptionResult("test", "whisper-1", "ru", text, [], 30)));

    private async Task<SessionDto> GetSessionAsync(Guid sessionId)
    {
        HttpResponseMessage response = await Client.GetAsync($"/trainer/sessions/{sessionId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private async Task<TrainerProgressDto> GetProgressAsync()
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<TrainerProgressDto>(response);
    }

    private async Task<IReadOnlyList<MistakeItemDto>> GetMistakesAsync()
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/mistakes");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(response);
    }

    private async Task<IReadOnlyList<AiUsageOperation>> GetUsageOpsAsync(Guid userId) =>
        await ExecuteInDbAsync(db => db.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .Select(r => r.Operation)
            .ToListAsync());

    /// <summary>Seeds a published topic with a FREE bank, then starts a LEARN session as a participant (PRO baseline).</summary>
    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartLearnAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync("voice-learn-topic");
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, questions);
    }

    /// <summary>Seeds a published topic with a FREE bank (4 local questions incl. OPEN_TEXT). Caller must be admin.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync(string slug)
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема голосового грейда", "Runtime", null, null, null, null));
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

    private async Task<Guid> CreateMockInterviewAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-interviews",
            new CreateMockInterviewRequest("net-junior-voice", "Найм: .NET junior", null, [topicId], null));
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
