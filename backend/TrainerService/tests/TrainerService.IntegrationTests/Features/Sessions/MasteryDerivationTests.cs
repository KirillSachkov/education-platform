using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     Derived topic mastery (#691): mastery = difficulty-weighted average of the LATEST score per
///     UNIQUE question (JUNIOR 0.7 / MIDDLE 1.0 / SENIOR 1.3), recomputed on every graded answer. This
///     kills farming (re-answering one question never inflates) and weights harder questions more. Driven
///     end-to-end through the real check endpoint (exercises <c>SessionAnswerGrading</c> recompute). PRO
///     baseline (GrantAll) so the SENIOR open answer is graded inline by the fake grader (CORRECT/100).
/// </summary>
public sealed class MasteryDerivationTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Mastery_is_difficulty_weighted_average_of_two_distinct_latest_scores()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions q) = await SeedPublishedTopicAsync();
        SessionDto session = await StartDrillAsync(topicId);

        // SENIOR (open, weight 1.3) answered correctly → 100; JUNIOR (single, weight 0.7) answered wrong → 0.
        await CheckAsync(session.Id, ItemFor(session, q.OpenQuestionId).Id,
            new CheckAnswerRequest(null, "Развёрнутый ответ про поколенческий GC."));
        await CheckAsync(session.Id, ItemFor(session, q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleWrongOption], null));

        // round((1.3*100 + 0.7*0) / (1.3 + 0.7)) = round(130 / 2.0) = 65, over two distinct questions.
        TopicMasteryDto mastery = await GetMasteryAsync(topicId);
        Assert.Equal(65, mastery.MasteryPercent);
        Assert.Equal(2, mastery.AnswersCount);
    }

    [Fact]
    public async Task Re_answering_the_same_question_does_not_change_mastery_or_answers_count()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions q) = await SeedPublishedTopicAsync();

        // Answer the SAME bank question correctly in three separate sessions. The question id is stable
        // across sessions, so it stays ONE distinct question → mastery 100, count 1, never inflated.
        for (int i = 0; i < 3; i++)
        {
            SessionDto session = await StartDrillAsync(topicId);
            CheckAnswerResponse result = await CheckAsync(
                session.Id, ItemFor(session, q.SingleQuestionId).Id,
                new CheckAnswerRequest([q.SingleCorrectOption], null));
            Assert.Equal("CORRECT", result.Verdict);

            TopicMasteryDto mastery = await GetMasteryAsync(topicId);
            Assert.Equal(100, mastery.MasteryPercent);
            Assert.Equal(1, mastery.AnswersCount);
        }
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

    private async Task<TopicMasteryDto> GetMasteryAsync(Guid topicId)
    {
        TrainerProgressDto dto = await ReadResultAsync<TrainerProgressDto>(
            await Client.GetAsync("/trainer/progress"));
        return Assert.Single(dto.Mastery, m => m.TopicId == topicId);
    }

    private async Task<SessionDto> StartDrillAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions", new StartSessionRequest("DRILL", topicId, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    /// <summary>Published topic + FREE bank + the canonical 4 questions; leaves the client a participant.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        HttpResponseMessage createResponse = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, "mastery-topic", "Тема для mastery", "Runtime", null, null, null, null));
        Guid topicId = (await ReadResultAsync<TopicIdResponse>(createResponse)).TopicId;

        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks", new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);

        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        return (topicId, questions);
    }
}
