using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Grade-at-end тест-сессия (#568 Ф2, RevealPolicy): END_OF_SESSION прячет правильный ответ до
///     Complete; PER_QUESTION (default) раскрывает сразу. Сверяет, что счёт всё равно копится «вслепую».
/// </summary>
public sealed class GradeAtEndTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task EndOfSession_check_does_not_reveal_answer_until_complete()
    {
        (Guid _, SessionDto session, var q) = await StartTestAsync("END_OF_SESSION");
        Assert.Equal("END_OF_SESSION", session.RevealPolicy);

        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        // The answer is graded server-side but the response is "accepted" (PENDING) — no leak.
        CheckAnswerResponse check = await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        Assert.Equal("PENDING", check.Verdict);
        Assert.Null(check.ScorePercent);
        Assert.Null(check.CorrectOptionIds);
        Assert.Null(check.ReferenceAnswer);
        Assert.Null(check.Explanation);

        // GET session while IN_PROGRESS — the answered item is marked answered but still hides the
        // correct answer AND the verdict/score (blind grade-at-end).
        HttpResponseMessage getResponse = await Client.GetAsync($"/trainer/sessions/{session.Id}");
        SessionDto inProgress = await ReadResultAsync<SessionDto>(getResponse);
        SessionItemDto answered = ItemFor(inProgress, q.SingleQuestionId);
        Assert.True(answered.IsAnswered);
        Assert.Null(answered.CorrectOptionIds);
        Assert.Null(answered.Verdict);
        Assert.Null(answered.ScorePercent);

        string rawInProgress = await ReadRawAsync(getResponse);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, rawInProgress, StringComparison.Ordinal);

        // Complete → now the answers/score are revealed.
        HttpResponseMessage completeResponse =
            await Client.PostAsync($"/trainer/sessions/{session.Id}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        SessionSummaryDto summary = await ReadResultAsync<SessionSummaryDto>(completeResponse);
        Assert.Equal("COMPLETED", summary.Status);
        // 1 correct of 3 auto-graded (multi + exact unanswered → count as 0); OPEN excluded → 33%.
        Assert.Equal(33, summary.ScorePercent);

        HttpResponseMessage afterResponse = await Client.GetAsync($"/trainer/sessions/{session.Id}");
        SessionDto completed = await ReadResultAsync<SessionDto>(afterResponse);
        SessionItemDto revealed = ItemFor(completed, q.SingleQuestionId);
        Assert.Equal([q.SingleCorrectOption], revealed.CorrectOptionIds); // now visible
    }

    [Fact]
    public async Task PerQuestion_check_reveals_answer_immediately()
    {
        (Guid _, SessionDto session, var q) = await StartTestAsync("PER_QUESTION");
        Assert.Equal("PER_QUESTION", session.RevealPolicy);

        SessionItemDto single = ItemFor(session, q.SingleQuestionId);
        CheckAnswerResponse check = await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        Assert.Equal("CORRECT", check.Verdict);
        Assert.Equal(100, check.ScorePercent);
        Assert.Equal([q.SingleCorrectOption], check.CorrectOptionIds);
    }

    [Fact]
    public async Task Default_reveal_policy_is_per_question_for_drill()
    {
        // Omitting RevealPolicy → PER_QUESTION (preserves historical instant DRILL).
        (Guid _, SessionDto session, _) = await StartTestAsync(revealPolicy: null);
        Assert.Equal("PER_QUESTION", session.RevealPolicy);
    }

    [Fact]
    public async Task Invalid_reveal_policy_is_rejected()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null, RevealPolicy: "WHENEVER"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.session.invalid.reveal.policy", await ReadErrorCodeAsync(response));
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

    private async Task<(Guid TopicId, SessionDto Session, TrainerQuestionFixtures.SeededQuestions Questions)> StartTestAsync(string? revealPolicy)
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null, revealPolicy));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, questions);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "test-topic", "Тема теста", "Runtime", null, null, null, null));
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
}
