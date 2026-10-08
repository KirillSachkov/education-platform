using System.Net;
using System.Net.Http.Json;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Sessions;

/// <summary>
///     GET /trainer/sessions/my (history list + mode/track filters, own-data only) and
///     GET /trainer/sessions/{id}/stats (per-difficulty + per-topic breakdown).
/// </summary>
public sealed class SessionHistoryAndStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task GetMySessions_lists_own_sessions_newest_first_with_counts()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");

        // Two drill sessions; in the second, answer one question so AnsweredCount differs.
        SessionDto first = await StartDrillAsync(topicId);
        SessionDto second = await StartDrillAsync(topicId);
        SessionItemDto single = second.Items.Single(i => i.QuestionId == q.SingleQuestionId);
        await CheckAsync(second.Id, single.Id, new CheckAnswerRequest([q.SingleCorrectOption], null));

        IReadOnlyList<SessionHistoryItemDto> history = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync("/trainer/sessions/my"));

        Assert.Equal(2, history.Count);
        // Newest first — the second-started session is on top.
        Assert.Equal(second.Id, history[0].Id);
        Assert.Equal(first.Id, history[1].Id);

        SessionHistoryItemDto top = history[0];
        Assert.Equal("DRILL", top.Mode);
        Assert.Equal("IN_PROGRESS", top.Status);
        Assert.Equal(4, top.TotalCount);
        Assert.Equal(1, top.AnsweredCount);
        Assert.Contains(topicId, top.TopicIds);
        // История несёт статус AI-грейдинга (#568): без открытых ответов он NOT_REQUIRED.
        // Фронт по PENDING/GRADING показывает «ИИ проверяет…» на вкладке симуляций.
        Assert.Equal("NOT_REQUIRED", top.GradingStatus);
    }

    [Fact]
    public async Task GetMySessions_exposes_time_limit_for_mock_and_null_for_drill()
    {
        // One published FREE topic on a track → start a MOCK (with timer) and a DRILL (no timer).
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        (Guid topicId, _) = await CreatePublishedTopicAsync(trackId, "timer-topic");

        AuthenticateAs("platform-participant");

        HttpResponseMessage mockResponse = await Client.PostAsJsonAsync(
            "/trainer/mock-sessions",
            new StartMockSessionRequest(trackId, QuestionCount: 50, TimeLimitSeconds: 1800, Difficulty: null));
        Assert.Equal(HttpStatusCode.OK, mockResponse.StatusCode);
        SessionDto mock = await ReadResultAsync<SessionDto>(mockResponse);
        SessionDto drill = await StartDrillAsync(topicId);

        IReadOnlyList<SessionHistoryItemDto> history = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync("/trainer/sessions/my"));

        SessionHistoryItemDto mockItem = Assert.Single(history, h => h.Id == mock.Id);
        Assert.Equal("MOCK", mockItem.Mode);
        Assert.Equal(1800, mockItem.TimeLimitSeconds);

        SessionHistoryItemDto drillItem = Assert.Single(history, h => h.Id == drill.Id);
        Assert.Equal("DRILL", drillItem.Mode);
        Assert.Null(drillItem.TimeLimitSeconds);
    }

    [Fact]
    public async Task GetMySessions_filters_by_mode()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");
        await StartDrillAsync(topicId);

        // Only DRILL exists → filtering by MOCK yields nothing, by DRILL yields the one session.
        IReadOnlyList<SessionHistoryItemDto> mock = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync("/trainer/sessions/my?mode=MOCK"));
        Assert.Empty(mock);

        IReadOnlyList<SessionHistoryItemDto> drill = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync("/trainer/sessions/my?mode=DRILL"));
        Assert.Single(drill);
    }

    [Fact]
    public async Task GetMySessions_filters_by_track()
    {
        // Two tracks, one drill session under the first track only.
        AuthenticateAsAdmin();
        Guid trackA = await CreateTrackAsync("dotnet", ".NET", "CSHARP");
        Guid trackB = await CreateTrackAsync("ts", "TypeScript", "TYPESCRIPT");
        (Guid topicA, _) = await CreatePublishedTopicAsync(trackA, "a-topic");

        AuthenticateAs("platform-participant");
        await StartDrillAsync(topicA);

        IReadOnlyList<SessionHistoryItemDto> underA = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync($"/trainer/sessions/my?trackId={trackA}"));
        Assert.Single(underA);

        IReadOnlyList<SessionHistoryItemDto> underB = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync($"/trainer/sessions/my?trackId={trackB}"));
        Assert.Empty(underB);
    }

    [Fact]
    public async Task GetMySessions_does_not_return_other_users_sessions()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant", Guid.NewGuid());
        await StartDrillAsync(topicId);

        // A different user sees an empty history.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        IReadOnlyList<SessionHistoryItemDto> history = await ReadResultAsync<IReadOnlyList<SessionHistoryItemDto>>(
            await Client.GetAsync("/trainer/sessions/my"));
        Assert.Empty(history);
    }

    [Fact]
    public async Task GetSessionStats_breaks_down_by_difficulty_and_topic()
    {
        (Guid topicId, var q) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");
        SessionDto session = await StartDrillAsync(topicId);

        // The canonical set has: SINGLE(JUNIOR), MULTI(MIDDLE), EXACT(JUNIOR), OPEN(SENIOR).
        // Answer SINGLE correct (JUNIOR → 1/1 graded correct) and MULTI wrong (MIDDLE → 0/1).
        await CheckAsync(
            session.Id, session.Items.Single(i => i.QuestionId == q.SingleQuestionId).Id,
            new CheckAnswerRequest([q.SingleCorrectOption], null));
        await CheckAsync(
            session.Id, session.Items.Single(i => i.QuestionId == q.MultiQuestionId).Id,
            new CheckAnswerRequest([q.MultiWrongOption], null));

        SessionStatsDto stats = await ReadResultAsync<SessionStatsDto>(
            await Client.GetAsync($"/trainer/sessions/{session.Id}/stats"));

        Assert.Equal(4, stats.TotalItems);
        Assert.Equal(2, stats.AnsweredItems);
        Assert.Equal(2, stats.GradedItems); // SINGLE + MULTI are auto-graded
        Assert.Equal(1, stats.CorrectItems); // only SINGLE was correct

        // Per-difficulty: JUNIOR has SINGLE(correct) + EXACT(unanswered) → 1 correct / 1 graded / 2 total.
        SessionBreakdownDto junior = Assert.Single(stats.ByDifficulty, b => b.Key == "JUNIOR");
        Assert.Equal(1, junior.Correct);
        Assert.Equal(1, junior.Graded);
        Assert.Equal(2, junior.Total);

        SessionBreakdownDto middle = Assert.Single(stats.ByDifficulty, b => b.Key == "MIDDLE");
        Assert.Equal(0, middle.Correct);
        Assert.Equal(1, middle.Graded);
        Assert.Equal(1, middle.Total);

        SessionBreakdownDto senior = Assert.Single(stats.ByDifficulty, b => b.Key == "SENIOR");
        Assert.Equal(0, senior.Correct);
        Assert.Equal(0, senior.Graded); // OPEN_TEXT, unanswered
        Assert.Equal(1, senior.Total);

        // Per-topic: single topic carries all 4 items, 1 correct / 2 graded / 4 total.
        SessionBreakdownDto topic = Assert.Single(stats.ByTopic, b => b.Key == topicId.ToString());
        Assert.Equal(1, topic.Correct);
        Assert.Equal(2, topic.Graded);
        Assert.Equal(4, topic.Total);
    }

    [Fact]
    public async Task GetSessionStats_of_other_user_returns_404()
    {
        (Guid topicId, _) = await SeedPublishedFreeTopicAsync();
        AuthenticateAs("platform-participant");
        SessionDto session = await StartDrillAsync(topicId);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await Client.GetAsync($"/trainer/sessions/{session.Id}/stats");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- helpers ---

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема", "Runtime", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> CreatePublishedTopicAsync(Guid trackId, string slug)
    {
        Guid topicId = await CreateTopicAsync(trackId, slug);
        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks",
            new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);
        return (topicId, questions);
    }

    /// <summary>Seeds a published FREE topic on a fresh track. Leaves client as admin.</summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedPublishedFreeTopicAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        return await CreatePublishedTopicAsync(trackId, "drill-topic");
    }

    private async Task<SessionDto> StartDrillAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null));
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
