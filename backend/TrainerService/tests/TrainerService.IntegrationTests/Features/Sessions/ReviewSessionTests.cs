using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain.Questions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     REVIEW-session start (#568 Ф2): a LEARN test built from an arbitrary set of questionIds
///     («Доучить» mistakes / «Пройти тест по закладке»). Covers cross-topic pooling, order
///     preservation, skipping inaccessible/missing ids, the freemium gate, the no-leak invariant,
///     and that the existing check flow drives the resulting session.
/// </summary>
public sealed class ReviewSessionTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task StartReview_builds_LEARN_session_from_arbitrary_questions_across_topics()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();
        AuthenticateAs("platform-participant");

        // Pick questions from BOTH topics + a non-existent id (must be dropped).
        HttpResponseMessage response = await StartReviewRawAsync(
        [
            setup.TopicBQuestion.Id,          // topic B
            setup.Questions.MultiQuestionId,  // topic A
            Guid.NewGuid(),                   // does not exist → skipped
        ]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);

        Assert.Equal("LEARN", session.Mode);
        Assert.Equal("PER_QUESTION", session.RevealPolicy);
        Assert.Equal("IN_PROGRESS", session.Status);

        // Only the two resolvable questions, IN THE GIVEN ORDER.
        Assert.Equal(
            new[] { setup.TopicBQuestion.Id, setup.Questions.MultiQuestionId },
            session.Items.OrderBy(i => i.SortIndex).Select(i => i.QuestionId).ToArray());

        // Each item carries its source topic; the two topics are represented.
        Assert.Equal(setup.TopicB, session.Items.Single(i => i.QuestionId == setup.TopicBQuestion.Id).TopicId);
        Assert.Equal(setup.TopicA, session.Items.Single(i => i.QuestionId == setup.Questions.MultiQuestionId).TopicId);
        Assert.Equal(new[] { setup.TopicA, setup.TopicB }.OrderBy(g => g), session.TopicIds.OrderBy(g => g));

        // No-leak on start (same invariant as LEARN/DRILL).
        Assert.All(session.Items, i => Assert.Null(i.CorrectOptionIds));
        string raw = await ReadRawAsync(response);
        Assert.DoesNotContain("gradingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewSession_is_driven_by_the_existing_check_flow_with_instant_reveal()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();
        AuthenticateAs("platform-participant");

        SessionDto session = await StartReviewAsync([setup.Questions.SingleQuestionId]);
        SessionItemDto item = session.Items.Single(i => i.QuestionId == setup.Questions.SingleQuestionId);

        // PER_QUESTION → wrong answer reveals the correct option instantly (formative).
        HttpResponseMessage checkResponse = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{session.Id}/answers/{item.Id}/check",
            new CheckAnswerRequest([setup.Questions.SingleWrongOption], null));
        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        CheckAnswerResponse result = await ReadResultAsync<CheckAnswerResponse>(checkResponse);

        Assert.Equal("INCORRECT", result.Verdict);
        Assert.Equal(0, result.ScorePercent);
        Assert.Equal([setup.Questions.SingleCorrectOption], result.CorrectOptionIds);
    }

    [Fact]
    public async Task StartReview_with_all_unknown_questions_returns_no_questions()
    {
        await SeedTrackWithTwoFreeTopicsAsync();
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await StartReviewRawAsync([Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.review.no.questions", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task StartReview_with_empty_set_is_rejected()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await StartReviewRawAsync([]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StartReview_skips_non_free_sample_questions_for_participant()
    {
        // Bank-tier is dormant (#674): a free participant can review FREE samples, but a non-free
        // question (OPEN_TEXT is never a free sample) is dropped → all-unresolvable → no questions.
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "paid", "Платная", "Runtime", "BACKEND");
        Guid bankId = await AddBankAsync(topicId, "PAID");
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll(); // free participant — only free samples are reviewable
        HttpResponseMessage response = await StartReviewRawAsync([questions.OpenQuestionId]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.review.no.questions", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task StartReview_allows_free_sample_questions_for_participant()
    {
        // The complement of the above: a free participant CAN review a free-sample question (#674).
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "free-rev", "Тема", "Runtime", "BACKEND");
        Guid bankId = await AddBankAsync(topicId, "PAID"); // tier irrelevant now
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll();
        SessionDto session = await StartReviewAsync([questions.SingleQuestionId]); // single is a free sample

        SessionItemDto item = Assert.Single(session.Items);
        Assert.Equal(questions.SingleQuestionId, item.QuestionId);
        Assert.False(item.IsLocked);
    }

    // --- helpers ---

    private sealed record TrackSetup(
        Guid TrackId, Guid TopicA, Guid TopicB,
        TrainerQuestionFixtures.SeededQuestions Questions,
        TrainerQuestion TopicBQuestion);

    private async Task<SessionDto> StartReviewAsync(IReadOnlyList<Guid> questionIds)
    {
        HttpResponseMessage response = await StartReviewRawAsync(questionIds);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private Task<HttpResponseMessage> StartReviewRawAsync(IReadOnlyList<Guid> questionIds) =>
        Client.PostAsJsonAsync("/trainer/review-sessions", new StartReviewSessionRequest(questionIds));

    /// <summary>
    ///     Track with two published FREE topics: topic A → canonical 4-question set, topic B →
    ///     a distinct single question. Leaves the client authenticated as admin.
    /// </summary>
    private async Task<TrackSetup> SeedTrackWithTwoFreeTopicsAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        Guid topicA = await CreateTopicAsync(trackId, "topic-a", "Тема A", "Runtime", "BACKEND");
        Guid bankA = await AddBankAsync(topicA, "FREE");
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankA);
        await Client.PostAsync($"/trainer/topics/{topicA}/publish", null);

        Guid topicB = await CreateTopicAsync(trackId, "topic-b", "Тема B", "Web", "BACKEND");
        Guid bankB = await AddBankAsync(topicB, "FREE");
        TrainerQuestion topicBQuestion =
            await TrainerQuestionFixtures.SeedSingleChoiceAsync(Factory, bankB, "Вопрос темы B");
        await Client.PostAsync($"/trainer/topics/{topicB}/publish", null);

        return new TrackSetup(trackId, topicA, topicB, questions, topicBQuestion);
    }

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug, string title, string area, string? direction)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, title, area, null, direction, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<Guid> AddBankAsync(Guid topicId, string tier)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest(tier, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicBankIdResponse>(response)).BankId;
    }
}
