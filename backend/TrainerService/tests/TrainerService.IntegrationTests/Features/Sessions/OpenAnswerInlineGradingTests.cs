using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.MockInterviews;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Inline AI grading of OPEN_TEXT answers in NON-mock sessions (#568 W2): a LEARN/DRILL/review
///     session grades the open answer at check-time via the (faked) <see cref="FakeOpenAnswerGrader"/> —
///     score + verdict + feedback, mastery + study-state updated, revealed under PER_QUESTION. The
///     fallback (grader fails) leaves the answer PENDING with no mastery/study-state change and a 200
///     response. MOCK sessions do NOT grade inline (the open answer stays PENDING at check-time; the
///     background MockAnswerGradingService handles it after Complete).
/// </summary>
public sealed class OpenAnswerInlineGradingTests(IntegrationTestsWebFactory factory)
    : TrainerServiceTestsBase(factory)
{
    // --- (a) LEARN open answer graded inline + mastery + study-state, revealed (PER_QUESTION) ---

    [Fact]
    public async Task LearnCheck_open_answer_is_AI_graded_inline_and_updates_mastery_and_study_state()
    {
        // Fake grades any non-empty open answer CORRECT/100 with feedback (default verdict CORRECT).
        Factory.OpenAnswerGrader.Feedback = "Раскрыта суть поколенческого GC.";

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Поколения 0/1/2; выжившие продвигаются."));

        // PER_QUESTION → the AI verdict/score/feedback + reference are revealed immediately.
        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(100, result.ScorePercent);
        Assert.Equal("Раскрыта суть поколенческого GC.", result.Feedback);
        Assert.Equal(TrainerQuestionFixtures.OpenReference, result.ReferenceAnswer);
        Assert.Equal(1, Factory.OpenAnswerGrader.CallCount);

        // Mastery EWMA updated (first answer = score = 100) and study-state KNOWN (studiedCount=1).
        TrainerProgressDto progress = await GetProgressAsync();
        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(100, topic.MasteryPercent);
        Assert.Equal(1, topic.AnswersCount);
        Assert.Equal(1, topic.StudiedCount);
        Assert.Equal(0, topic.MistakesCount);

        // The graded open answer surfaces in the session review with verdict/score/feedback.
        SessionDto fetched = await GetSessionAsync(session.Id);
        SessionItemDto answered = ItemFor(fetched, q.OpenQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Equal("CORRECT", answered.Verdict);
        Assert.Equal(100, answered.ScorePercent);
        Assert.Equal("Раскрыта суть поколенческого GC.", answered.Feedback);
    }

    [Fact]
    public async Task LearnCheck_open_answer_graded_correct_below_100_counts_as_known_not_mistake()
    {
        // Regression: the AI grades an open answer CORRECT but with a partial-completeness score (< 100).
        // Study-state must follow the VERDICT (→ KNOWN), NOT `score == 100` — otherwise a «Верно» open
        // answer was silently filed as a mistake and the topic «не засчитывалась» (prod: verdict=CORRECT,
        // score=10 → study_status=WRONG). Both the typed and the voice path share this recording flow.
        Factory.OpenAnswerGrader.Verdict = TrainerService.Domain.AnswerVerdict.CORRECT;
        Factory.OpenAnswerGrader.ScorePercent = 85;
        Factory.OpenAnswerGrader.Feedback = "Суть верна, не хватило деталей.";

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "В целом верно, но кратко."));
        Assert.Equal("CORRECT", result.Verdict);
        Assert.Equal(85, result.ScorePercent);

        // Counted as studied/known — NOT a mistake — even though the score is below 100.
        TrainerProgressDto progress = await GetProgressAsync();
        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(1, topic.StudiedCount);
        Assert.Equal(0, topic.MistakesCount);

        // And it must NOT surface in «Мои ошибки».
        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        Assert.Empty(mistakes);
    }

    [Fact]
    public async Task LearnCheck_open_answer_graded_wrong_records_study_state_as_mistake()
    {
        Factory.OpenAnswerGrader.Verdict = TrainerService.Domain.AnswerVerdict.INCORRECT;
        Factory.OpenAnswerGrader.ScorePercent = 0;
        Factory.OpenAnswerGrader.Feedback = "Не по теме.";

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Что-то совсем не то."));
        Assert.Equal("INCORRECT", result.Verdict);
        Assert.Equal(0, result.ScorePercent);

        // A wrong open answer registers a WRONG study-state → surfaces in «Мои ошибки».
        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        MistakeItemDto mistake = Assert.Single(mistakes);
        Assert.Equal(q.OpenQuestionId, mistake.QuestionId);
        Assert.Equal(topicId, mistake.TopicId);
    }

    // --- (b) fallback: grader fails → PENDING, no mastery/study-state change, 200 ---

    [Fact]
    public async Task LearnCheck_open_answer_falls_back_to_PENDING_on_grader_failure_without_touching_mastery()
    {
        Factory.OpenAnswerGrader.FailNext = true; // grader returns a failure Result

        (Guid topicId, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{open.Id}/check",
            new CheckAnswerRequest(null, "Развёрнутый ответ про GC."));

        // Endpoint still returns 200 — an AI outage must not 500 the check.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckAnswerResponse result = await ReadResultAsync<CheckAnswerResponse>(response);
        Assert.Equal("PENDING", result.Verdict);
        Assert.Null(result.ScorePercent);
        Assert.Equal("Проверка ответа временно недоступна", result.Feedback);

        // No mastery row for this topic (score was null → mastery untouched, retryable).
        TrainerProgressDto progress = await GetProgressAsync();
        Assert.DoesNotContain(progress.Mastery, m => m.TopicId == topicId && m.AnswersCount > 0);

        // No study-state mistake recorded (the answer can be retried after the AI recovers).
        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        Assert.Empty(mistakes);
    }

    [Fact]
    public async Task LearnCheck_open_answer_falls_back_to_PENDING_when_grader_throws()
    {
        // The throw-sentinel makes the fake grader throw — the inline path must catch and degrade.
        (Guid _, SessionDto session, var q) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, q.OpenQuestionId);

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{open.Id}/check",
            new CheckAnswerRequest(null, FakeOpenAnswerGrader.ThrowSentinel));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CheckAnswerResponse result = await ReadResultAsync<CheckAnswerResponse>(response);
        Assert.Equal("PENDING", result.Verdict);
        Assert.Null(result.ScorePercent);
    }

    [Fact]
    public async Task Complete_non_mock_session_keeps_inline_grade_without_background_regrading()
    {
        Factory.OpenAnswerGrader.ScorePercent = 80;

        (Guid _, SessionDto session, var questions) = await StartLearnAsync();
        SessionItemDto open = ItemFor(session, questions.OpenQuestionId);
        await CheckAsync(
            session.Id,
            open.Id,
            new CheckAnswerRequest(null, "Поколения 0/1/2; выжившие объекты продвигаются."));

        HttpResponseMessage completeResponse = await Client.PostAsync(
            $"/trainer/sessions/{session.Id}/complete",
            null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        SessionSummaryDto summary = await ReadResultAsync<SessionSummaryDto>(completeResponse);

        // Three unanswered auto-gradable questions count as zero; the already graded open answer
        // contributes 80, so the final score is 80 / 4 = 20. It must not enter the MOCK grader.
        Assert.Equal(20, summary.ScorePercent);
        Assert.Equal("NOT_REQUIRED", summary.GradingStatus);
        await Task.Delay(100);
        Assert.Equal(1, Factory.OpenAnswerGrader.CallCount);
    }

    // --- (c) MOCK open answer is NOT graded inline (still PENDING at check-time) ---

    [Fact]
    public async Task MockCheck_open_answer_is_not_graded_inline()
    {
        // MOCK must skip inline grading; we assert the grader is never invoked (CallCount == 0).
        AuthenticateAsAdmin();
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync("mock-inline-topic");
        Guid mockId = await CreateMockInterviewAsync(topicId);
        SessionDto session = await StartMockInterviewAsync(mockId);
        Assert.Equal("MOCK", session.Mode);

        SessionItemDto open = ItemFor(session, q.OpenQuestionId);
        CheckAnswerResponse result = await CheckAsync(
            session.Id, open.Id, new CheckAnswerRequest(null, "Устный ответ про поколенческий GC."));

        // MOCK: the open answer is recorded PENDING and the inline grader is never called.
        Assert.Equal("PENDING", result.Verdict);
        Assert.Null(result.ScorePercent);
        Assert.Null(result.Feedback);
        Assert.Equal(0, Factory.OpenAnswerGrader.CallCount);
    }

    // --- helpers ---

    private static SessionItemDto ItemFor(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId);

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

    private async Task<TrainerProgressDto> GetProgressAsync()
    {
        HttpResponseMessage response = await Client.GetAsync("/trainer/progress");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<TrainerProgressDto>(response);
    }

    /// <summary>Seeds a published topic with a FREE bank, then starts a LEARN session as a participant.</summary>
    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartLearnAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync("inline-learn-topic");
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
            new CreateTopicRequest(trackId, slug, "Тема инлайн-грейда", "Runtime", null, null, null, null));
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
            new CreateMockInterviewRequest("net-junior-inline", "Найм: .NET junior", null, [topicId], null));
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
