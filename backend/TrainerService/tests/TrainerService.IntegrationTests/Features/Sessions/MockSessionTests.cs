using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Progress;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Domain;
using TrainerService.Domain.Questions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     MOCK-session start: cross-topic question pooling over a track, stored timer, freemium
///     gate (FREE vs PAID), difficulty filter, per-topic mastery on check, and the no-leak
///     invariant (start response carries no correct answers / grading key).
/// </summary>
public sealed class MockSessionTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task StartMock_pools_questions_across_topics_stores_timer_and_hides_answers()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();

        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(setup.TrackId, QuestionCount: 50, TimeLimitSeconds: 1800, Difficulty: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);

        Assert.Equal("MOCK", session.Mode);
        Assert.Equal("IN_PROGRESS", session.Status);
        Assert.Equal(1800, session.TimeLimitSeconds);

        // Pool spans both topics → 4 (topic A bank) + 1 (topic B bank) = 5 questions, both
        // topics represented and each item carries its source topicId.
        Assert.Equal(5, session.Items.Count);
        Assert.Contains(session.Items, i => i.TopicId == setup.TopicA);
        Assert.Contains(session.Items, i => i.TopicId == setup.TopicB);
        Assert.Equal(new[] { setup.TopicA, setup.TopicB }.OrderBy(g => g),
            session.TopicIds.OrderBy(g => g));

        // Every item exposes its difficulty (level badge) even before being answered.
        Assert.All(session.Items, i =>
        {
            Assert.False(i.IsAnswered);
            Assert.NotNull(i.Difficulty);
            Assert.Null(i.CorrectOptionIds);
            Assert.Null(i.ReferenceAnswer);
            Assert.Null(i.Explanation);
        });

        // No-leak: raw body must not carry the grading key blob nor secret reference texts.
        string raw = await ReadRawAsync(response);
        Assert.DoesNotContain("gradingKey", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(TrainerQuestionFixtures.ExactReference, raw, StringComparison.Ordinal);
        Assert.DoesNotContain(TrainerQuestionFixtures.OpenReference, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartMock_respects_requested_question_count()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();

        AuthenticateAs("platform-participant");
        SessionDto session = await StartMockAsync(setup.TrackId, questionCount: 3);

        Assert.Equal(3, session.Items.Count);
    }

    [Fact]
    public async Task StartMock_difficulty_filter_keeps_only_matching_questions()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();

        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(setup.TrackId, QuestionCount: 50, TimeLimitSeconds: null, Difficulty: "JUNIOR"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);

        Assert.NotEmpty(session.Items);
        Assert.All(session.Items, i => Assert.Equal("JUNIOR", i.Difficulty));
    }

    [Fact]
    public async Task StartMock_updates_mastery_of_the_correct_source_topic()
    {
        TrackSetup setup = await SeedTrackWithTwoFreeTopicsAsync();

        AuthenticateAs("platform-participant");
        SessionDto session = await StartMockAsync(setup.TrackId, questionCount: 50);

        // Answer the topic-B question correctly. Only topic B's mastery must move.
        SessionItemDto topicBItem = session.Items.Single(i => i.QuestionId == setup.TopicBQuestion.Id);
        Assert.Equal(setup.TopicB, topicBItem.TopicId);

        Guid correctOption = setup.TopicBQuestion.Options.First(o => o.IsCorrect).Id;
        CheckAnswerResponse checkResult = await CheckAsync(
            session.Id, topicBItem.Id, new CheckAnswerRequest([correctOption], null));
        Assert.Equal("CORRECT", checkResult.Verdict);

        TrainerProgressDto progress = await ReadResultAsync<TrainerProgressDto>(
            await Client.GetAsync("/trainer/progress"));

        TopicMasteryDto topicB = Assert.Single(progress.Mastery, m => m.TopicId == setup.TopicB);
        Assert.Equal(100, topicB.MasteryPercent);
        Assert.Equal(1, topicB.AnswersCount);

        // Topic A was never answered → no mastery row for it.
        Assert.DoesNotContain(progress.Mastery, m => m.TopicId == setup.TopicA);
    }

    [Fact]
    public async Task StartMock_requires_pro_for_participant()
    {
        // Mock is PRO-only (#614): a free participant (no cap:TRAINER_PRO) is rejected before pooling.
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "paid", "Платная", "Runtime", "BACKEND");
        Guid bankId = await AddBankAsync(topicId, "PAID");
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        AuthenticateAs("platform-participant");
        EntitlementChecker.DenyAll(); // free participant — mock is PRO-only
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("trainer.pro.required", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task StartMock_includes_paid_topics_for_admin()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "paid", "Платная", "Runtime", "BACKEND");
        Guid bankId = await AddBankAsync(topicId, "PAID");
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        // Admin can see PAID banks → mock starts with the bank's questions.
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        SessionDto session = await ReadResultAsync<SessionDto>(response);
        Assert.Equal(4, session.Items.Count);
        Assert.All(session.Items, i => Assert.Equal(topicId, i.TopicId));
    }

    [Fact]
    public async Task StartMock_on_track_without_published_topics_returns_no_questions()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        await Client.PostAsync($"/trainer/tracks/{trackId}/publish", null);
        // No topics at all.

        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("trainer.mock.no.questions", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task StartMock_on_unknown_track_returns_404()
    {
        AuthenticateAs("platform-participant");
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(Guid.NewGuid(), QuestionCount: 10, TimeLimitSeconds: null, Difficulty: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("trainer.track.not.found", await ReadErrorCodeAsync(response));
    }

    // --- helpers ---

    private sealed record TrackSetup(Guid TrackId, Guid TopicA, Guid TopicB, TrainerQuestion TopicBQuestion);

    /// <summary>
    ///     Builds a track with two published FREE topics: topic A seeded with the canonical
    ///     4-question set, topic B with a single distinct SINGLE_CHOICE question. Leaves the
    ///     client authenticated as admin.
    /// </summary>
    private async Task<TrackSetup> SeedTrackWithTwoFreeTopicsAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();

        Guid topicA = await CreateTopicAsync(trackId, "topic-a", "Тема A", "Runtime", "BACKEND");
        Guid bankA = await AddBankAsync(topicA, "FREE");
        await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankA);
        await Client.PostAsync($"/trainer/topics/{topicA}/publish", null);

        Guid topicB = await CreateTopicAsync(trackId, "topic-b", "Тема B", "Web", "BACKEND");
        Guid bankB = await AddBankAsync(topicB, "FREE");
        TrainerQuestion topicBQuestion = await TrainerQuestionFixtures.SeedSingleChoiceAsync(
            Factory, bankB, "Вопрос темы B", QuestionDifficulty.MIDDLE, "web");
        await Client.PostAsync($"/trainer/topics/{topicB}/publish", null);

        return new TrackSetup(trackId, topicA, topicB, topicBQuestion);
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

    private async Task<SessionDto> StartMockAsync(Guid trackId, int questionCount)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, questionCount, TimeLimitSeconds: null, Difficulty: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private async Task<CheckAnswerResponse> CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<CheckAnswerResponse>(response);
    }
}
