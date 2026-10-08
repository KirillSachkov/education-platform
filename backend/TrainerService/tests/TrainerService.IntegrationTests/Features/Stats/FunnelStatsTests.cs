using System.Net;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Admin;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/funnel?days=N (#681 T3): admin-only funnel rollups over
///     training_sessions + training_session_items — start→complete rate (overall + by mode),
///     drop-off by question position, and abandoned mocks. Sessions/items are seeded directly in the
///     DB. Asserts every bucket plus the 403 for non-admins.
/// </summary>
public sealed class FunnelStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private enum SessionEnd
    {
        InProgress,
        Completed,
        Abandoned,
    }

    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats/funnel?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats/funnel?days=30");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_profile_returns_zeroed_funnel()
    {
        AuthenticateAsAdmin();

        AdminFunnelStatsDto stats = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=30"));

        Assert.Equal(30, stats.Days);

        Assert.Equal(0, stats.Completion.Started);
        Assert.Equal(0, stats.Completion.Completed);
        Assert.Equal(0d, stats.Completion.Rate);

        // All three modes always present, zero-filled.
        Assert.Equal(3, stats.CompletionByMode.Count);
        Assert.All(stats.CompletionByMode, m => Assert.Equal(0, m.Started));

        Assert.Empty(stats.DropOffByPosition);

        Assert.Equal(0, stats.AbandonedMocks.MockStarted);
        Assert.Equal(0, stats.AbandonedMocks.MockAbandoned);
        Assert.Equal(0d, stats.AbandonedMocks.AbandonRate);
    }

    [Fact]
    public async Task Completion_rate_overall_and_by_mode()
    {
        // DRILL: 1 completed + 1 in-progress. LEARN: 1 completed. MOCK: 1 in-progress.
        await SeedSessionAsync(TrainingMode.DRILL, SessionEnd.Completed);
        await SeedSessionAsync(TrainingMode.DRILL, SessionEnd.InProgress);
        await SeedSessionAsync(TrainingMode.LEARN, SessionEnd.Completed);
        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.InProgress);

        AuthenticateAsAdmin();
        AdminFunnelStatsDto stats = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=30"));

        Assert.Equal(4, stats.Completion.Started);
        Assert.Equal(2, stats.Completion.Completed);
        Assert.Equal(0.5d, stats.Completion.Rate);

        AdminModeCompletionDto drill = Assert.Single(stats.CompletionByMode, m => m.Mode == "DRILL");
        Assert.Equal(2, drill.Started);
        Assert.Equal(1, drill.Completed);
        Assert.Equal(0.5d, drill.Rate);

        AdminModeCompletionDto learn = Assert.Single(stats.CompletionByMode, m => m.Mode == "LEARN");
        Assert.Equal(1, learn.Started);
        Assert.Equal(1, learn.Completed);
        Assert.Equal(1d, learn.Rate);

        AdminModeCompletionDto mock = Assert.Single(stats.CompletionByMode, m => m.Mode == "MOCK");
        Assert.Equal(1, mock.Started);
        Assert.Equal(0, mock.Completed);
        Assert.Equal(0d, mock.Rate);
    }

    [Fact]
    public async Task Drop_off_by_position_counts_reached_and_answered()
    {
        // Session 1: 3 items, first two answered (drops at position 2).
        await SeedSessionWithItemsAsync(itemCount: 3, answeredCount: 2);
        // Session 2: 3 items, only the first answered (drops at position 1).
        await SeedSessionWithItemsAsync(itemCount: 3, answeredCount: 1);

        AuthenticateAsAdmin();
        AdminFunnelStatsDto stats = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=30"));

        Assert.Equal(3, stats.DropOffByPosition.Count);

        // Ordered by position ascending.
        Assert.Equal([0, 1, 2], stats.DropOffByPosition.Select(p => p.Position).ToArray());

        AdminDropOffPositionDto p0 = stats.DropOffByPosition[0];
        Assert.Equal(2, p0.Reached);
        Assert.Equal(2, p0.Answered);
        Assert.Equal(1d, p0.AnsweredRate);

        AdminDropOffPositionDto p1 = stats.DropOffByPosition[1];
        Assert.Equal(2, p1.Reached);
        Assert.Equal(1, p1.Answered);
        Assert.Equal(0.5d, p1.AnsweredRate);

        AdminDropOffPositionDto p2 = stats.DropOffByPosition[2];
        Assert.Equal(2, p2.Reached);
        Assert.Equal(0, p2.Answered);
        Assert.Equal(0d, p2.AnsweredRate);
    }

    [Fact]
    public async Task Abandoned_mocks_count_in_progress_and_abandoned()
    {
        // MOCK: 1 completed, 1 in-progress, 1 explicitly abandoned. DRILL in-progress must NOT count.
        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.Completed);
        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.InProgress);
        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.Abandoned);
        await SeedSessionAsync(TrainingMode.DRILL, SessionEnd.InProgress);

        AuthenticateAsAdmin();
        AdminFunnelStatsDto stats = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=30"));

        Assert.Equal(3, stats.AbandonedMocks.MockStarted);
        Assert.Equal(2, stats.AbandonedMocks.MockAbandoned); // in-progress + abandoned, not the completed one
        Assert.Equal(2d / 3d, stats.AbandonedMocks.AbandonRate, 5);
    }

    [Fact]
    public async Task Window_excludes_sessions_older_than_days()
    {
        DateTime now = DateTime.UtcNow;

        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.InProgress, now.AddDays(-2));    // in window
        await SeedSessionAsync(TrainingMode.MOCK, SessionEnd.InProgress, now.AddDays(-100));  // out of window

        AuthenticateAsAdmin();
        AdminFunnelStatsDto stats = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=30"));

        Assert.Equal(1, stats.AbandonedMocks.MockStarted);
        Assert.Equal(1, stats.Completion.Started);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        AdminFunnelStatsDto dflt = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel"));
        Assert.Equal(30, dflt.Days);

        AdminFunnelStatsDto clamped = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=99999"));
        Assert.Equal(365, clamped.Days);

        AdminFunnelStatsDto floored = await ReadResultAsync<AdminFunnelStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/funnel?days=0"));
        Assert.Equal(1, floored.Days);
    }

    // --- helpers ---

    private async Task SeedSessionAsync(TrainingMode mode, SessionEnd end, DateTime? startedAt = null)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                Guid.NewGuid(), mode, trackId: null, [Guid.NewGuid()],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            switch (end)
            {
                case SessionEnd.Completed:
                    session.Complete(100);
                    await db.SaveChangesAsync();
                    break;
                case SessionEnd.Abandoned:
                    session.Abandon();
                    await db.SaveChangesAsync();
                    break;
                case SessionEnd.InProgress:
                default:
                    break;
            }

            return session.Id;
        });

        if (startedAt.HasValue)
            await ExecuteInDbAsync(db =>
                db.Database.ExecuteSqlAsync(
                    $"UPDATE trainer.training_sessions SET started_at = {startedAt.Value} WHERE id = {sessionId}"));
    }

    /// <summary>Seeds a DRILL session with <paramref name="itemCount"/> items, answering the first <paramref name="answeredCount"/>.</summary>
    private async Task SeedSessionWithItemsAsync(int itemCount, int answeredCount) =>
        await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                Guid.NewGuid(), TrainingMode.DRILL, trackId: null, [Guid.NewGuid()],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;

            for (int i = 0; i < itemCount; i++)
                session.AddItem(
                    questionId: Guid.NewGuid(),
                    topicId: Guid.NewGuid(),
                    questionType: "SINGLE_CHOICE",
                    questionText: $"Q{i}",
                    optionsJson: "[]",
                    section: null,
                    difficulty: "JUNIOR",
                    sortIndex: i,
                    gradingKeyJson: null);

            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            var items = session.Items.OrderBy(it => it.SortIndex).ToList();
            for (int i = 0; i < answeredCount; i++)
                session.RecordAnswer(items[i].Id, "x", 100, AnswerVerdict.CORRECT, null);

            await db.SaveChangesAsync();
            return session.Id;
        });
}
