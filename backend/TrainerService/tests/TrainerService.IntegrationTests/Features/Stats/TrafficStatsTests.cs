using System.Net;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Admin;
using TrainerService.Domain;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/traffic?days=N (#681 T3): admin-only traffic rollups over
///     training_sessions — DAU/WAU/MAU rolling windows, first-touch retention (D1/D7/D30),
///     new-vs-returning per day, and sessions/day by mode. Sessions are seeded directly in the DB so
///     started_at can be backdated across calendar days. Asserts every bucket plus the 403 for non-admins.
/// </summary>
public sealed class TrafficStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats/traffic?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_gets_401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats/traffic?days=30");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_profile_returns_zeroed_traffic()
    {
        AuthenticateAsAdmin();

        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(30, stats.Days);

        Assert.Equal(0, stats.ActiveUsers.Dau);
        Assert.Equal(0, stats.ActiveUsers.Wau);
        Assert.Equal(0, stats.ActiveUsers.Mau);

        Assert.Equal(0, stats.Retention.D1.CohortSize);
        Assert.Equal(0, stats.Retention.D1.ReturnedCount);
        Assert.Equal(0d, stats.Retention.D1.Rate);
        Assert.Equal(0, stats.Retention.D30.CohortSize);

        // Dense series present even with no data: days + 1 zero-filled points.
        Assert.Equal(31, stats.NewVsReturning.Count);
        Assert.All(stats.NewVsReturning, p => Assert.Equal(0, p.NewUsers + p.ReturningUsers));

        Assert.Equal(31, stats.SessionsByMode.Count);
        Assert.All(stats.SessionsByMode, p => Assert.Equal(0, p.Drill + p.Learn + p.Mock));
    }

    [Fact]
    public async Task Active_users_count_distinct_in_rolling_windows()
    {
        DateTime now = DateTime.UtcNow;

        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddHours(-2));   // DAU + WAU + MAU
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-3));    // WAU + MAU
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-15));   // MAU only
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-45));   // outside all windows

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(1, stats.ActiveUsers.Dau);
        Assert.Equal(2, stats.ActiveUsers.Wau);
        Assert.Equal(3, stats.ActiveUsers.Mau);
    }

    [Fact]
    public async Task Active_users_count_a_user_once_across_many_sessions()
    {
        DateTime now = DateTime.UtcNow;
        Guid user = Guid.NewGuid();

        // Same user, three sessions today → DAU/WAU/MAU each count the user exactly once.
        await SeedSessionAsync(user, TrainingMode.DRILL, now.AddHours(-1));
        await SeedSessionAsync(user, TrainingMode.LEARN, now.AddHours(-2));
        await SeedSessionAsync(user, TrainingMode.MOCK, now.AddHours(-3));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(1, stats.ActiveUsers.Dau);
        Assert.Equal(1, stats.ActiveUsers.Wau);
        Assert.Equal(1, stats.ActiveUsers.Mau);
    }

    [Fact]
    public async Task Sessions_by_mode_daily_is_dense_and_zero_filled()
    {
        DateTime now = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now);
        DateOnly twoDaysAgo = today.AddDays(-2);

        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now);
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now);
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.MOCK, now);
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.LEARN, now.AddDays(-2));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(31, stats.SessionsByMode.Count);

        AdminSessionsByModeDayDto todayPoint = Assert.Single(stats.SessionsByMode, p => p.Date == today);
        Assert.Equal(2, todayPoint.Drill);
        Assert.Equal(1, todayPoint.Mock);
        Assert.Equal(0, todayPoint.Learn);

        AdminSessionsByModeDayDto twoDaysAgoPoint = Assert.Single(stats.SessionsByMode, p => p.Date == twoDaysAgo);
        Assert.Equal(1, twoDaysAgoPoint.Learn);
        Assert.Equal(0, twoDaysAgoPoint.Drill);

        // Totals across the whole dense series.
        Assert.Equal(2, stats.SessionsByMode.Sum(p => p.Drill));
        Assert.Equal(1, stats.SessionsByMode.Sum(p => p.Mock));
        Assert.Equal(1, stats.SessionsByMode.Sum(p => p.Learn));
    }

    [Fact]
    public async Task New_vs_returning_splits_first_touch_from_subsequent()
    {
        DateTime now = DateTime.UtcNow;
        DateOnly today = DateOnly.FromDateTime(now);
        DateOnly threeDaysAgo = today.AddDays(-3);

        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();

        // userA: first session 3 days ago, returns today. userB: brand-new today.
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-3));
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddHours(-1));
        await SeedSessionAsync(userB, TrainingMode.DRILL, now.AddHours(-2));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        AdminNewReturningDayDto threeAgo = Assert.Single(stats.NewVsReturning, p => p.Date == threeDaysAgo);
        Assert.Equal(1, threeAgo.NewUsers);        // userA's first-ever touch
        Assert.Equal(0, threeAgo.ReturningUsers);

        AdminNewReturningDayDto todayPoint = Assert.Single(stats.NewVsReturning, p => p.Date == today);
        Assert.Equal(1, todayPoint.NewUsers);       // userB
        Assert.Equal(1, todayPoint.ReturningUsers); // userA returning

        Assert.Equal(2, stats.NewVsReturning.Sum(p => p.NewUsers));
        Assert.Equal(1, stats.NewVsReturning.Sum(p => p.ReturningUsers));
    }

    [Fact]
    public async Task Retention_d1_counts_only_next_day_returners()
    {
        DateTime now = DateTime.UtcNow;

        // userA: first 5 days ago, returns the very next day (fday+1) → D1 returner.
        Guid userA = Guid.NewGuid();
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-5));
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-4));

        // userB: first 5 days ago, never returns → in cohort, not retained.
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-5));

        // userC: first today → too recent for a D1 cohort (needs a full next-day window).
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddHours(-1));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(2, stats.Retention.D1.CohortSize);
        Assert.Equal(1, stats.Retention.D1.ReturnedCount);
        Assert.Equal(0.5d, stats.Retention.D1.Rate);

        // fday = today-5 is too recent for the D7/D30 cohorts (needs ≥7 / ≥30 elapsed days).
        Assert.Equal(0, stats.Retention.D7.CohortSize);
        Assert.Equal(0, stats.Retention.D30.CohortSize);
    }

    [Fact]
    public async Task Retention_d7_counts_returns_within_seven_days()
    {
        DateTime now = DateTime.UtcNow;

        // userA: first 10 days ago, returns 4 days later (within 7) → D7 returner.
        Guid userA = Guid.NewGuid();
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-10));
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-6));

        // userB: first 10 days ago, returns 9 days later (outside 7) → in cohort, not retained for D7.
        Guid userB = Guid.NewGuid();
        await SeedSessionAsync(userB, TrainingMode.DRILL, now.AddDays(-10));
        await SeedSessionAsync(userB, TrainingMode.DRILL, now.AddDays(-1));

        // userC: first 10 days ago, never returns.
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-10));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        Assert.Equal(3, stats.Retention.D7.CohortSize);
        Assert.Equal(1, stats.Retention.D7.ReturnedCount);
        Assert.Equal(1d / 3d, stats.Retention.D7.Rate, 5);

        // None of these returns lands exactly on fday+1, so D1 retained = 0 (cohort still 3).
        Assert.Equal(3, stats.Retention.D1.CohortSize);
        Assert.Equal(0, stats.Retention.D1.ReturnedCount);
    }

    [Fact]
    public async Task Retention_d30_requires_full_window_elapsed()
    {
        DateTime now = DateTime.UtcNow;

        // days=60 so a 30-day return window can be measured for users first seen 40 days ago.
        // userA: first 40 days ago, returns 20 days later (within 30) → D30 returner.
        Guid userA = Guid.NewGuid();
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-40));
        await SeedSessionAsync(userA, TrainingMode.DRILL, now.AddDays(-20));

        // userB: first 40 days ago, never returns → in cohort, not retained.
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-40));

        // userC: first 10 days ago → too recent for the D30 cohort.
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-10));

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=60"));

        Assert.Equal(60, stats.Days);
        Assert.Equal(2, stats.Retention.D30.CohortSize);
        Assert.Equal(1, stats.Retention.D30.ReturnedCount);
        Assert.Equal(0.5d, stats.Retention.D30.Rate);
    }

    [Fact]
    public async Task Window_excludes_sessions_older_than_days()
    {
        DateTime now = DateTime.UtcNow;

        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-2));    // in window
        await SeedSessionAsync(Guid.NewGuid(), TrainingMode.DRILL, now.AddDays(-100));  // out of window

        AuthenticateAsAdmin();
        AdminTrafficStatsDto stats = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=30"));

        // Only the in-window session shows in the dense by-mode series.
        Assert.Equal(1, stats.SessionsByMode.Sum(p => p.Drill));
        // The 100-day-old user is outside MAU (30 days) too.
        Assert.Equal(1, stats.ActiveUsers.Mau);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        // No query → default 30 → 31 dense points per series.
        AdminTrafficStatsDto dflt = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic"));
        Assert.Equal(30, dflt.Days);
        Assert.Equal(31, dflt.NewVsReturning.Count);
        Assert.Equal(31, dflt.SessionsByMode.Count);

        // Over the max → clamped to 365.
        AdminTrafficStatsDto clamped = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=99999"));
        Assert.Equal(365, clamped.Days);

        // Below the min → clamped to 1 → 2 dense points.
        AdminTrafficStatsDto floored = await ReadResultAsync<AdminTrafficStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/traffic?days=0"));
        Assert.Equal(1, floored.Days);
        Assert.Equal(2, floored.NewVsReturning.Count);
        Assert.Equal(2, floored.SessionsByMode.Count);
    }

    // --- helpers ---

    /// <summary>
    ///     Seeds a session for a user/mode and backdates started_at so the rolling windows, retention
    ///     cohorts and daily series can be exercised across calendar days.
    /// </summary>
    private async Task SeedSessionAsync(Guid userId, TrainingMode mode, DateTime startedAt)
    {
        Guid sessionId = await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, mode, trackId: null, [Guid.NewGuid()],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();
            return session.Id;
        });

        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.training_sessions SET started_at = {startedAt} WHERE id = {sessionId}"));
    }
}
