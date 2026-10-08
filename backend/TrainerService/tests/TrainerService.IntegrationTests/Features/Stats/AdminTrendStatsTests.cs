using System.Net;
using TrainerService.Contracts.Admin;
using TrainerService.Domain.Snapshots;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats/trends?days=N (#681 T6): admin-only owner KPI curve from
///     <c>daily_stat_snapshots</c>. Asserts the dense daily series (days+1 contiguous points, oldest-first),
///     zero-fill of gap days, micro→₽ cost conversion, the days clamp/default, exclusion of out-of-window
///     days, the all-zero empty window, and that a non-admin gets 403. Past days are seeded (never today)
///     so the once-at-startup snapshot job can't collide on the unique snapshot-date.
/// </summary>
public sealed class AdminTrendStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats/trends?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dense_series_zero_fills_gaps_and_converts_cost()
    {
        DateOnly d1 = Today.AddDays(-1);
        DateOnly d3 = Today.AddDays(-3);
        await SeedDailyAsync(d1, sessions: 5, activeUsers: 3, completed: 2, openGrades: 1, costMicro: 2_500_000L, avgAcc: 75.0);
        await SeedDailyAsync(d3, sessions: 9, activeUsers: 4, completed: 6, openGrades: 2, costMicro: 1_000_000L, avgAcc: 50.0);

        AuthenticateAsAdmin();
        AdminTrendStatsDto dto = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends?days=30"));

        Assert.Equal(30, dto.Days);
        Assert.Equal(31, dto.Points.Count); // cutoff day .. today inclusive

        // Contiguous + oldest-first window.
        Assert.Equal(Today.AddDays(-30), dto.Points[0].Date);
        Assert.Equal(Today, dto.Points[^1].Date);
        for (int i = 1; i < dto.Points.Count; i++)
            Assert.Equal(dto.Points[i - 1].Date.AddDays(1), dto.Points[i].Date);

        AdminTrendPointDto p1 = Assert.Single(dto.Points, p => p.Date == d1);
        Assert.Equal(5, p1.SessionsStarted);
        Assert.Equal(3, p1.ActiveUsers);
        Assert.Equal(2, p1.CompletedSessions);
        Assert.Equal(2_500_000L, p1.CostMicroRub);
        Assert.Equal(2.5m, p1.CostRub); // micro → ₽
        Assert.Equal(75.0, p1.AvgAccuracyPct, 3);

        AdminTrendPointDto p3 = Assert.Single(dto.Points, p => p.Date == d3);
        Assert.Equal(9, p3.SessionsStarted);
        Assert.Equal(1_000_000L, p3.CostMicroRub);
        Assert.Equal(1.0m, p3.CostRub);

        // A day with no snapshot is zero-filled, not missing.
        AdminTrendPointDto gap = Assert.Single(dto.Points, p => p.Date == Today.AddDays(-2));
        Assert.Equal(0, gap.SessionsStarted);
        Assert.Equal(0, gap.ActiveUsers);
        Assert.Equal(0, gap.CompletedSessions);
        Assert.Equal(0L, gap.CostMicroRub);
        Assert.Equal(0m, gap.CostRub);
        Assert.Equal(0d, gap.AvgAccuracyPct, 3);
    }

    [Fact]
    public async Task Days_outside_the_window_are_excluded()
    {
        DateOnly inWindow = Today.AddDays(-5);
        DateOnly outWindow = Today.AddDays(-60);
        await SeedDailyAsync(inWindow, sessions: 7, activeUsers: 2, completed: 3, openGrades: 1, costMicro: 700_000L, avgAcc: 60.0);
        await SeedDailyAsync(outWindow, sessions: 100, activeUsers: 50, completed: 80, openGrades: 20, costMicro: 99_000_000L, avgAcc: 90.0);

        AuthenticateAsAdmin();
        AdminTrendStatsDto dto = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends?days=30"));

        Assert.Equal(31, dto.Points.Count);
        Assert.DoesNotContain(dto.Points, p => p.Date == outWindow);

        AdminTrendPointDto inP = Assert.Single(dto.Points, p => p.Date == inWindow);
        Assert.Equal(7, inP.SessionsStarted);

        // The out-of-window day's large numbers never leak into the series.
        Assert.DoesNotContain(dto.Points, p => p.SessionsStarted == 100);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        AdminTrendStatsDto dflt = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends"));
        Assert.Equal(30, dflt.Days);
        Assert.Equal(31, dflt.Points.Count);

        AdminTrendStatsDto clamped = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends?days=99999"));
        Assert.Equal(365, clamped.Days);
        Assert.Equal(366, clamped.Points.Count);

        AdminTrendStatsDto floored = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends?days=0"));
        Assert.Equal(1, floored.Days);
        Assert.Equal(2, floored.Points.Count); // cutoff day + today
    }

    [Fact]
    public async Task Empty_window_is_all_zero_points()
    {
        AuthenticateAsAdmin();
        AdminTrendStatsDto dto = await ReadResultAsync<AdminTrendStatsDto>(
            await Client.GetAsync("/trainer/admin/stats/trends?days=7"));

        Assert.Equal(8, dto.Points.Count);
        Assert.All(dto.Points, p =>
        {
            Assert.Equal(0, p.SessionsStarted);
            Assert.Equal(0, p.ActiveUsers);
            Assert.Equal(0, p.CompletedSessions);
            Assert.Equal(0L, p.CostMicroRub);
            Assert.Equal(0m, p.CostRub);
            Assert.Equal(0d, p.AvgAccuracyPct);
        });
    }

    // --- helpers ---

    private async Task SeedDailyAsync(
        DateOnly date,
        int sessions,
        int activeUsers,
        int completed,
        int openGrades,
        long costMicro,
        double avgAcc) =>
        await ExecuteInDbAsync(async db =>
        {
            db.DailyStatSnapshots.Add(DailyStatSnapshot.Create(
                date, sessions, activeUsers, completed, openGrades, costMicro, avgAcc));
            return await db.SaveChangesAsync();
        });
}
