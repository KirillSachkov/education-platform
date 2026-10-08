using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Stats;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/stats/activity (#568): per-day session/answered/correct timeline + current/longest
///     streak. Sessions are seeded directly in the DB so started_at dates can be controlled.
/// </summary>
public sealed class ActivityStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task Activity_is_empty_for_a_fresh_profile()
    {
        AuthenticateAs("platform-participant");

        TrainerActivityDto activity = await ReadResultAsync<TrainerActivityDto>(
            await Client.GetAsync("/trainer/stats/activity"));

        Assert.Empty(activity.Days);
        Assert.Equal(0, activity.CurrentStreak);
        Assert.Equal(0, activity.LongestStreak);
    }

    [Fact]
    public async Task Activity_groups_by_day_with_answered_and_correct_counts()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();
        DateTime today = DateTime.UtcNow.Date;

        // One session today with 2 answered items (1 correct), one session yesterday with 1 answered correct.
        await SeedSessionAsync(userId, topicId, today.AddHours(10), (100, AnswerVerdict.CORRECT), (0, AnswerVerdict.INCORRECT));
        await SeedSessionAsync(userId, topicId, today.AddDays(-1).AddHours(9), (100, AnswerVerdict.CORRECT));

        AuthenticateAs("platform-participant", userId);
        TrainerActivityDto activity = await ReadResultAsync<TrainerActivityDto>(
            await Client.GetAsync("/trainer/stats/activity?days=30"));

        Assert.Equal(2, activity.Days.Count);

        TrainerActivityDayDto todayRow = Assert.Single(activity.Days, d => d.Date == DateOnly.FromDateTime(today));
        Assert.Equal(1, todayRow.Sessions);
        Assert.Equal(2, todayRow.Answered);
        Assert.Equal(1, todayRow.Correct);
        Assert.Equal(50, todayRow.AccuracyPercent);

        TrainerActivityDayDto yesterdayRow = Assert.Single(
            activity.Days, d => d.Date == DateOnly.FromDateTime(today.AddDays(-1)));
        Assert.Equal(1, yesterdayRow.Sessions);
        Assert.Equal(1, yesterdayRow.Answered);
        Assert.Equal(1, yesterdayRow.Correct);
        Assert.Equal(100, yesterdayRow.AccuracyPercent);

        // Two consecutive days ending today → current streak 2; longest also 2.
        Assert.Equal(2, activity.CurrentStreak);
        Assert.Equal(2, activity.LongestStreak);
    }

    [Fact]
    public async Task Activity_streak_breaks_on_a_gap_and_window_does_not_bound_longest()
    {
        Guid userId = Guid.NewGuid();
        Guid topicId = Guid.NewGuid();
        DateTime today = DateTime.UtcNow.Date;

        // A 3-day run far in the past (outside the 30-day window) and a single active day today.
        await SeedSessionAsync(userId, topicId, today.AddDays(-100).AddHours(8), (100, AnswerVerdict.CORRECT));
        await SeedSessionAsync(userId, topicId, today.AddDays(-99).AddHours(8), (100, AnswerVerdict.CORRECT));
        await SeedSessionAsync(userId, topicId, today.AddDays(-98).AddHours(8), (100, AnswerVerdict.CORRECT));
        await SeedSessionAsync(userId, topicId, today.AddHours(8), (100, AnswerVerdict.CORRECT));

        AuthenticateAs("platform-participant", userId);
        TrainerActivityDto activity = await ReadResultAsync<TrainerActivityDto>(
            await Client.GetAsync("/trainer/stats/activity?days=30"));

        // Window (30 days) only shows today's activity; the old run is outside it.
        Assert.Single(activity.Days);
        // Current streak = 1 (today only — yesterday had no session). Longest = 3 (the old run, all-time).
        Assert.Equal(1, activity.CurrentStreak);
        Assert.Equal(3, activity.LongestStreak);
    }

    // --- helpers ---

    /// <summary>
    ///     Seeds a completed DRILL session with the given answered items, then patches started_at so the
    ///     activity timeline / streaks can be exercised across calendar days.
    /// </summary>
    private async Task SeedSessionAsync(
        Guid userId,
        Guid topicId,
        DateTime startedAt,
        params (int Score, AnswerVerdict Verdict)[] answers)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, TrainingMode.DRILL, trackId: null, [topicId],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;

            for (int i = 0; i < answers.Length; i++)
                session.AddItem(
                    questionId: Guid.NewGuid(),
                    topicId: topicId,
                    questionType: "SINGLE_CHOICE",
                    questionText: $"Q{i}",
                    optionsJson: "[]",
                    section: null,
                    difficulty: "JUNIOR",
                    sortIndex: i,
                    gradingKeyJson: null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            // Record answers on the persisted items (ids assigned by the value generator).
            var items = session.Items.OrderBy(it => it.SortIndex).ToList();
            for (int i = 0; i < answers.Length; i++)
                session.RecordAnswer(items[i].Id, "x", answers[i].Score, answers[i].Verdict, null);

            session.Complete(answers.Length == 0 ? 0 : (int)answers.Average(a => a.Score));
            await db.SaveChangesAsync();
            return session.Id;
        });

        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_sessions SET started_at = {startedAt} WHERE id = {sessionId}"));
    }
}
