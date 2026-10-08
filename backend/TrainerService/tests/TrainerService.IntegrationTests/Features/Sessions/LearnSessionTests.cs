using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Questions;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     LEARN-сессия «обучение по тестам» (#568 Ф2): formative, instant-feedback (PER_QUESTION),
///     апсерт <c>QuestionStudyState</c> на каждый проверенный ответ (питает список/ошибки/SRS).
/// </summary>
public sealed class LearnSessionTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task StartLearn_creates_LEARN_session_per_question_reveal_without_leak()
    {
        (Guid _, SessionDto session, HttpResponseMessage startResponse, _) = await StartLearnAsync();

        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        Assert.Equal("LEARN", session.Mode);
        Assert.Equal("PER_QUESTION", session.RevealPolicy);
        Assert.Equal(4, session.Items.Count);
        Assert.All(session.Items, i =>
        {
            Assert.False(i.IsAnswered);
            Assert.Null(i.CorrectOptionIds);
        });

        // No-leak on start (same invariant as DRILL).
        string raw = await ReadRawAsync(startResponse);
        Assert.DoesNotContain("gradingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LearnCheck_reveals_instantly_and_records_study_state()
    {
        (Guid topicId, SessionDto session, _, var q) = await StartLearnAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        // Wrong answer → instant reveal of the correct option (formative feedback).
        CheckAnswerResponse result = await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleWrongOption], null));
        Assert.Equal("INCORRECT", result.Verdict);
        Assert.Equal(0, result.ScorePercent);
        Assert.Equal([q.SingleCorrectOption], result.CorrectOptionIds);

        // The wrong answer registered a QuestionStudyState (WRONG) → it surfaces in «Мои ошибки».
        HttpResponseMessage mistakesResponse = await Client.GetAsync("/trainer/mistakes");
        IReadOnlyList<MistakeItemDto> mistakes = await ReadResultAsync<IReadOnlyList<MistakeItemDto>>(mistakesResponse);
        MistakeItemDto mistake = Assert.Single(mistakes);
        Assert.Equal(q.SingleQuestionId, mistake.QuestionId);
        Assert.Equal(topicId, mistake.TopicId);
    }

    [Fact]
    public async Task LearnCheck_correct_answer_marks_study_state_KNOWN()
    {
        (Guid topicId, SessionDto session, _, var q) = await StartLearnAsync();
        SessionItemDto single = ItemFor(session, q.SingleQuestionId);

        await CheckAsync(
            session.Id, single.Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));

        // KNOWN study-state → topic now shows studiedCount in progress analytics.
        HttpResponseMessage progressResponse = await Client.GetAsync("/trainer/progress");
        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(progressResponse);
        TopicMasteryDto topic = Assert.Single(progress.Mastery, m => m.TopicId == topicId);
        Assert.Equal(1, topic.StudiedCount);
        Assert.Equal(0, topic.MistakesCount);
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

    private async Task<(Guid TopicId, SessionDto Session, HttpResponseMessage Response, TrainerQuestionFixtures.SeededQuestions Questions)> StartLearnAsync()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions questions) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/learn-sessions",
            new StartLearnSessionRequest(topicId, QuestionCount: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        return (topicId, session, response, questions);
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "learn-topic", "Тема обучения", "Runtime", null, null, null, null));
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
