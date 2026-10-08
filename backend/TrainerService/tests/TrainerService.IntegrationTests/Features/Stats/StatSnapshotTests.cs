using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrainerService.Contracts.Sessions;
using TrainerService.Contracts.Topics;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.Domain.Snapshots;
using TrainerService.Infrastructure.Postgres;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     Daily stat-snapshot job + repository (#681 T1). Covers the snapshot tick writing ≥1 row per table
///     from seeded sessions/items/masteries (+ ai_usage cost), idempotent re-run for the same date, the
///     zero-activity branch, and all three repository read methods T6 consumes.
/// </summary>
public sealed class StatSnapshotTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task SnapshotTick_writes_kpi_mastery_and_question_rows_from_seeded_activity()
    {
        (Guid topicId, TrainerQuestionFixtures.SeededQuestions q) = await SeedActivityAsync();

        StatSnapshotRunResult result = await RunSnapshotAsync(Today);

        // ≥1 row per table.
        Assert.Equal(1, result.DailyRows);
        Assert.True(result.TopicMasteryRows >= 1, "expected at least one topic-mastery snapshot row");
        Assert.True(result.QuestionAccuracyRows >= 1, "expected at least one question-accuracy snapshot row");

        // --- platform KPIs ---
        DailyStatSnapshot daily = Assert.Single(await GetDailySeriesAsync(Today, Today));
        Assert.Equal(Today, daily.SnapshotDate);
        Assert.Equal(2, daily.SessionsStarted);   // two drill sessions
        Assert.Equal(1, daily.ActiveUsers);        // one participant
        Assert.Equal(1, daily.CompletedSessions);  // session1 completed; session2 left in progress
        Assert.Equal(1, daily.OpenGrades);         // one graded OPEN_TEXT answer
        Assert.Equal(50, daily.AvgAccuracyPct, 3); // graded scores today: 100, 50, 0 → avg 50

        // Cost is summed from the ai_usage ledger; the seeded 1.5M row is included (inline open-grade
        // ledger rows may add more — exact-sum aggregation is covered deterministically in its own test).
        Assert.True(daily.TotalCostMicroRub >= 1_500_000L, "expected the seeded ledger cost to be included");

        // --- per-question accuracy ---
        QuestionAccuracySnapshot single = Assert.Single(
            await GetQuestionAccuracySeriesAsync(q.SingleQuestionId, Today, Today));
        Assert.Equal(2, single.Attempts);   // answered correct (s1) + wrong (s2)
        Assert.Equal(1, single.Correct);
        Assert.Equal(50, single.AccuracyPct, 3);

        QuestionAccuracySnapshot open = Assert.Single(
            await GetQuestionAccuracySeriesAsync(q.OpenQuestionId, Today, Today));
        Assert.Equal(1, open.Attempts);
        Assert.Equal(1, open.Correct);       // fake grader verdict CORRECT (score 50, but verdict drives "correct")
        Assert.Equal(100, open.AccuracyPct, 3);

        // --- per-topic mastery (snapshot copies the current mastery value) ---
        TopicMasterySnapshot mastery = Assert.Single(
            (await GetUserTopicMasteryOnDatesAsync(CurrentUserId, Today, Today.AddDays(-1)))
                .Where(m => m.TopicId == topicId).ToList());
        int liveMastery = await ExecuteInDbAsync(db =>
            db.TopicMasteries.Where(m => m.UserId == CurrentUserId && m.TopicId == topicId)
                .Select(m => m.MasteryPercent).SingleAsync());
        Assert.Equal(liveMastery, mastery.Mastery, 3);
    }

    [Fact]
    public async Task SnapshotTick_is_idempotent_for_same_date()
    {
        await SeedActivityAsync();

        StatSnapshotRunResult first = await RunSnapshotAsync(Today);
        StatSnapshotRunResult second = await RunSnapshotAsync(Today);

        // Same counts both runs — re-running replaces, never duplicates.
        Assert.Equal(first.DailyRows, second.DailyRows);
        Assert.Equal(first.TopicMasteryRows, second.TopicMasteryRows);
        Assert.Equal(first.QuestionAccuracyRows, second.QuestionAccuracyRows);

        // The DB holds exactly one set of today's rows after two runs.
        (int dailyCount, int masteryCount, int questionCount) = await ExecuteInDbAsync(async db => (
            await db.DailyStatSnapshots.CountAsync(s => s.SnapshotDate == Today),
            await db.TopicMasterySnapshots.CountAsync(s => s.SnapshotDate == Today),
            await db.QuestionAccuracySnapshots.CountAsync(s => s.SnapshotDate == Today)));

        Assert.Equal(1, dailyCount);
        Assert.Equal(second.TopicMasteryRows, masteryCount);
        Assert.Equal(second.QuestionAccuracyRows, questionCount);
    }

    [Fact]
    public async Task SnapshotTick_with_no_activity_writes_zero_daily_row_and_no_detail_rows()
    {
        StatSnapshotRunResult result = await RunSnapshotAsync(Today);

        Assert.Equal(1, result.DailyRows);
        Assert.Equal(0, result.TopicMasteryRows);
        Assert.Equal(0, result.QuestionAccuracyRows);

        DailyStatSnapshot daily = Assert.Single(await GetDailySeriesAsync(Today, Today));
        Assert.Equal(0, daily.SessionsStarted);
        Assert.Equal(0, daily.ActiveUsers);
        Assert.Equal(0, daily.CompletedSessions);
        Assert.Equal(0, daily.OpenGrades);
        Assert.Equal(0L, daily.TotalCostMicroRub);
        Assert.Equal(0, daily.AvgAccuracyPct, 3);
    }

    [Fact]
    public async Task SnapshotTick_sums_ai_usage_cost_for_the_day()
    {
        // Direct ledger rows only (no sessions / inline grades) → deterministic cost sum.
        await InsertAiUsageAsync(Guid.NewGuid(), 1_000_000L);
        await InsertAiUsageAsync(Guid.NewGuid(), 250_000L);

        await RunSnapshotAsync(Today);

        DailyStatSnapshot daily = Assert.Single(await GetDailySeriesAsync(Today, Today));
        Assert.Equal(1_250_000L, daily.TotalCostMicroRub);
    }

    [Fact]
    public async Task GetDailySeries_returns_rows_in_range_and_empty_outside()
    {
        await RunSnapshotAsync(Today);

        Assert.Single(await GetDailySeriesAsync(Today.AddDays(-3), Today));
        Assert.Empty(await GetDailySeriesAsync(Today.AddDays(1), Today.AddDays(5)));
    }

    [Fact]
    public async Task GetUserTopicMastery_returns_points_for_two_dates()
    {
        (Guid topicId, _) = await SeedActivityAsync();
        DateOnly monthAgo = Today.AddDays(-30);

        // The job snapshots CURRENT mastery under whatever date it runs for → simulate a month-ago point.
        await RunSnapshotAsync(monthAgo);
        await RunSnapshotAsync(Today);

        IReadOnlyList<TopicMasterySnapshot> points =
            await GetUserTopicMasteryOnDatesAsync(CurrentUserId, Today, monthAgo);

        List<TopicMasterySnapshot> forTopic = points.Where(p => p.TopicId == topicId).ToList();
        Assert.Equal(2, forTopic.Count);
        Assert.Contains(forTopic, p => p.SnapshotDate == Today);
        Assert.Contains(forTopic, p => p.SnapshotDate == monthAgo);
        Assert.All(forTopic, p => Assert.Equal(CurrentUserId, p.UserId));
    }

    [Fact]
    public async Task GetQuestionAccuracySeries_returns_question_rows_in_range()
    {
        (_, TrainerQuestionFixtures.SeededQuestions q) = await SeedActivityAsync();

        await RunSnapshotAsync(Today);

        Assert.Single(await GetQuestionAccuracySeriesAsync(q.SingleQuestionId, Today.AddDays(-1), Today));
        Assert.Empty(await GetQuestionAccuracySeriesAsync(q.SingleQuestionId, Today.AddDays(1), Today.AddDays(5)));
        // A question that was never answered has no accuracy snapshot.
        Assert.Empty(await GetQuestionAccuracySeriesAsync(q.MultiQuestionId, Today.AddDays(-1), Today));
    }

    // --- snapshot/repo helpers (resolve the repository in a scope, like ExecuteInDbAsync) ---

    private async Task<StatSnapshotRunResult> RunSnapshotAsync(DateOnly date)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();
        return await repo.WriteSnapshotsAsync(date);
    }

    private async Task<IReadOnlyList<DailyStatSnapshot>> GetDailySeriesAsync(DateOnly from, DateOnly to)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();
        return await repo.GetDailySeriesAsync(from, to);
    }

    private async Task<IReadOnlyList<TopicMasterySnapshot>> GetUserTopicMasteryOnDatesAsync(
        Guid userId, DateOnly dateA, DateOnly dateB)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();
        return await repo.GetUserTopicMasteryOnDatesAsync(userId, dateA, dateB);
    }

    private async Task<IReadOnlyList<QuestionAccuracySnapshot>> GetQuestionAccuracySeriesAsync(
        Guid questionId, DateOnly from, DateOnly to)
    {
        using IServiceScope scope = Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();
        return await repo.GetQuestionAccuracySeriesAsync(questionId, from, to);
    }

    // --- activity seeding ---

    /// <summary>
    ///     Seeds a published FREE topic, two drill sessions by one participant (SINGLE answered correct in
    ///     one + wrong in the other; OPEN answered with verdict CORRECT, score 50), completes one session,
    ///     and inserts a known ai_usage ledger row. Leaves the client authed as the participant.
    /// </summary>
    private async Task<(Guid TopicId, TrainerQuestionFixtures.SeededQuestions Questions)> SeedActivityAsync()
    {
        AuthenticateAsAdmin();
        Guid trackId = await CreateTrackAsync();
        Guid topicId = await CreateTopicAsync(trackId, "snapshot-topic");
        HttpResponseMessage bankResponse = await Client.PostAsJsonAsync(
            $"/trainer/topics/{topicId}/banks", new AddTopicBankRequest("FREE", null));
        Guid bankId = (await ReadResultAsync<TopicBankIdResponse>(bankResponse)).BankId;
        TrainerQuestionFixtures.SeededQuestions questions =
            await TrainerQuestionFixtures.SeedFourQuestionsAsync(Factory, bankId);
        await Client.PostAsync($"/trainer/topics/{topicId}/publish", null);

        // Open answers grade CORRECT but with score 50 → clean avg accuracy, verdict-driven correctness.
        Factory.OpenAnswerGrader.ScorePercent = 50;

        AuthenticateAs("platform-participant");

        SessionDto s1 = await StartDrillAsync(topicId);
        await CheckAsync(s1.Id, ItemId(s1, questions.SingleQuestionId),
            new CheckAnswerRequest([questions.SingleCorrectOption], null));
        await CheckAsync(s1.Id, ItemId(s1, questions.OpenQuestionId),
            new CheckAnswerRequest(null, "Поколенческий GC двигает выживших по поколениям 0/1/2."));
        await CompleteAsync(s1.Id);

        SessionDto s2 = await StartDrillAsync(topicId);
        await CheckAsync(s2.Id, ItemId(s2, questions.SingleQuestionId),
            new CheckAnswerRequest([questions.SingleWrongOption], null));

        await InsertAiUsageAsync(CurrentUserId, 1_500_000L);

        return (topicId, questions);
    }

    private static Guid ItemId(SessionDto session, Guid questionId) =>
        session.Items.Single(i => i.QuestionId == questionId).Id;

    private async Task<Guid> CreateTopicAsync(Guid trackId, string slug)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/topics",
            new CreateTopicRequest(trackId, slug, "Тема", "Runtime", null, null, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadResultAsync<TopicIdResponse>(response)).TopicId;
    }

    private async Task<SessionDto> StartDrillAsync(Guid topicId)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            "/trainer/sessions",
            new StartSessionRequest("DRILL", topicId, null, "PER_QUESTION"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadResultAsync<SessionDto>(response);
    }

    private async Task CheckAsync(Guid sessionId, Guid itemId, CheckAnswerRequest request)
    {
        HttpResponseMessage response = await Client.PostAsJsonAsync(
            $"/trainer/sessions/{sessionId}/answers/{itemId}/check", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task CompleteAsync(Guid sessionId)
    {
        HttpResponseMessage response = await Client.PostAsync($"/trainer/sessions/{sessionId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task InsertAiUsageAsync(Guid userId, long costMicroRub)
    {
        await ExecuteInDbAsync(async db =>
        {
            db.AiUsageRecords.Add(AiUsageRecord.Create(
                userId, AiUsageOperation.OPEN_ANSWER_GRADE, "test-model", 100, 20, 120, costMicroRub));
            await db.SaveChangesAsync();
            return true;
        });
    }
}
