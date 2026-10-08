using System.Net;
using Microsoft.EntityFrameworkCore;
using TrainerService.Contracts.Admin;
using TrainerService.Core.Features.Stats.UserLookup;
using TrainerService.Domain;
using TrainerService.Domain.AiUsage;
using TrainerService.Domain.TrainingSessions;
using TrainerService.IntegrationTests.Infrastructure;

namespace TrainerService.IntegrationTests.Features.Stats;

/// <summary>
///     GET /trainer/admin/stats?days=N (#614 D1): admin-only AI-spend + usage rollups over the
///     trainer.ai_usage ledger and training_sessions. Asserts totals, by-operation, by-model,
///     top-users, dense daily length and sessions-by-mode; and that a non-admin gets 403.
/// </summary>
public sealed class AdminStatsTests(IntegrationTestsWebFactory factory) : TrainerServiceTestsBase(factory)
{
    [Fact]
    public async Task NonAdmin_participant_gets_403()
    {
        AuthenticateAs("platform-participant");

        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats?days=30");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_gets_ai_spend_and_usage_rollups()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();

        // --- AI usage ledger: two ops on two models, two users, two days ---
        //   userA: open-grade gpt-4.1-mini  cost 30_000, in 200 / out 50
        //   userA: transcription whisper-1  cost 10_000, null tokens
        //   userB: mock-aggregate gpt-4.1   cost  5_000, in 100 / out 100
        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 200, 50, 250, 30_000L, sessionId);
        await SeedUsageAsync(userA, AiUsageOperation.TRANSCRIPTION, "whisper-1", null, null, null, 10_000L, sessionId);
        await SeedUsageAsync(userB, AiUsageOperation.MOCK_AGGREGATE, "gpt-4.1", 100, 100, 200, 5_000L, null);

        // --- usage: sessions by mode + a completed one + two distinct users ---
        await SeedSessionAsync(userA, TrainingMode.DRILL, complete: true);
        await SeedSessionAsync(userA, TrainingMode.LEARN, complete: false);
        await SeedSessionAsync(userB, TrainingMode.MOCK, complete: false);

        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        Assert.Equal(30, stats.Days);

        // --- AI spend totals ---
        Assert.Equal(45_000L, stats.AiSpend.TotalCostMicroRub);
        Assert.Equal(0.045m, stats.AiSpend.TotalCostRub);
        Assert.Equal(3, stats.AiSpend.TotalOperations);
        Assert.Equal(300, stats.AiSpend.TotalInputTokens);   // 200 + 0(null) + 100
        Assert.Equal(150, stats.AiSpend.TotalOutputTokens);  //  50 + 0(null) + 100

        // --- by operation (sorted by cost desc) ---
        Assert.Equal(3, stats.AiSpend.ByOperation.Count);
        AdminAiOperationBreakdownDto openGrade =
            Assert.Single(stats.AiSpend.ByOperation, o => o.Operation == "OPEN_ANSWER_GRADE");
        Assert.Equal(1, openGrade.Count);
        Assert.Equal(30_000L, openGrade.CostMicroRub);
        Assert.Equal("OPEN_ANSWER_GRADE", stats.AiSpend.ByOperation[0].Operation); // highest cost first

        // --- by model ---
        AdminAiModelBreakdownDto miniModel =
            Assert.Single(stats.AiSpend.ByModel, m => m.Model == "gpt-4.1-mini");
        Assert.Equal(1, miniModel.Count);
        Assert.Equal(30_000L, miniModel.CostMicroRub);
        Assert.Equal(200, miniModel.InputTokens);
        Assert.Equal(50, miniModel.OutputTokens);
        Assert.Equal(3, stats.AiSpend.ByModel.Count);

        // --- dense daily: days + 1 points (cutoff day .. today inclusive), summing to the total ---
        Assert.Equal(31, stats.AiSpend.Daily.Count);
        Assert.Equal(45_000L, stats.AiSpend.Daily.Sum(d => d.CostMicroRub));

        // --- top users (sorted by spend desc) ---
        Assert.Equal(2, stats.AiSpend.TopUsers.Count);
        Assert.Equal(userA, stats.AiSpend.TopUsers[0].UserId);
        Assert.Equal(40_000L, stats.AiSpend.TopUsers[0].CostMicroRub);
        Assert.Equal(2, stats.AiSpend.TopUsers[0].OperationCount);
        Assert.Equal(userB, stats.AiSpend.TopUsers[1].UserId);
        Assert.Equal(5_000L, stats.AiSpend.TopUsers[1].CostMicroRub);

        // --- usage ---
        Assert.Equal(3, stats.Usage.SessionsStarted);
        Assert.Equal(2, stats.Usage.ActiveUsers);
        Assert.Equal(1, stats.Usage.CompletedSessions);

        // by-mode: all three buckets always present (zero-filled), DRILL/LEARN/MOCK each = 1
        Assert.Equal(3, stats.Usage.ByMode.Count);
        Assert.Equal(1, stats.Usage.ByMode.Single(m => m.Mode == "DRILL").Count);
        Assert.Equal(1, stats.Usage.ByMode.Single(m => m.Mode == "LEARN").Count);
        Assert.Equal(1, stats.Usage.ByMode.Single(m => m.Mode == "MOCK").Count);
    }

    [Fact]
    public async Task Window_excludes_rows_older_than_days()
    {
        Guid userId = Guid.NewGuid();

        // One recent row (in window) and one backdated 60 days (out of the 30-day window).
        Guid recentId = await SeedUsageAsync(
            userId, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 10, 10, 20, 1_000L, null);
        Guid oldId = await SeedUsageAsync(
            userId, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 10, 10, 20, 9_000L, null);
        await BackdateUsageAsync(oldId, DateTimeOffset.UtcNow.AddDays(-60));
        _ = recentId;

        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        Assert.Equal(1_000L, stats.AiSpend.TotalCostMicroRub);
        Assert.Equal(1, stats.AiSpend.TotalOperations);
    }

    [Fact]
    public async Task Days_param_is_clamped_and_defaults_to_30()
    {
        AuthenticateAsAdmin();

        // No query → default 30 → 31 dense daily points.
        AdminStatsDto dflt = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats"));
        Assert.Equal(30, dflt.Days);
        Assert.Equal(31, dflt.AiSpend.Daily.Count);

        // Over the max → clamped to 365.
        AdminStatsDto clamped = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=99999"));
        Assert.Equal(365, clamped.Days);

        // Below the min → clamped to 1 → 2 dense daily points (cutoff day + today).
        AdminStatsDto floored = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=0"));
        Assert.Equal(1, floored.Days);
        Assert.Equal(2, floored.AiSpend.Daily.Count);
    }

    [Fact]
    public async Task Top_users_enriched_with_name_and_avatar_from_lookup()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();

        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 100, 50, 150, 30_000L, null);
        await SeedUsageAsync(userB, AiUsageOperation.MOCK_AGGREGATE, "gpt-4.1", 100, 100, 200, 5_000L, null);

        Guid avatarA = Guid.NewGuid();
        Factory.UserLookup.Users[userA] = new UserLookupDto("Alice", $"/api/files/{avatarA:D}/content");
        Factory.UserLookup.Users[userB] = new UserLookupDto("Bob", null); // resolved, but no avatar

        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        Assert.Equal(2, stats.AiSpend.TopUsers.Count);

        AdminAiTopUserDto top = stats.AiSpend.TopUsers[0]; // userA — higher spend
        Assert.Equal(userA, top.UserId);
        Assert.Equal("Alice", top.DisplayName);
        Assert.Equal($"/api/files/{avatarA:D}/content", top.AvatarUrl);

        AdminAiTopUserDto second = stats.AiSpend.TopUsers[1]; // userB
        Assert.Equal(userB, second.UserId);
        Assert.Equal("Bob", second.DisplayName);
        Assert.Null(second.AvatarUrl);
    }

    [Fact]
    public async Task Top_users_carry_ids_with_null_names_when_lookup_returns_empty()
    {
        Guid userA = Guid.NewGuid();
        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 10, 10, 20, 3_000L, null);
        // UserLookup left empty → no credit resolved for the id.

        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        AdminAiTopUserDto top = Assert.Single(stats.AiSpend.TopUsers);
        Assert.Equal(userA, top.UserId);
        Assert.Equal(3_000L, top.CostMicroRub);
        Assert.Null(top.DisplayName);
        Assert.Null(top.AvatarUrl);
    }

    [Fact]
    public async Task Top_users_soft_degrade_to_ids_when_lookup_throws()
    {
        Guid userA = Guid.NewGuid();
        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 10, 10, 20, 7_000L, null);

        Factory.UserLookup.ThrowOnCall = true; // AuthService down / blows up

        AuthenticateAsAdmin();
        HttpResponseMessage response = await Client.GetAsync("/trainer/admin/stats?days=30");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // still 200 — enrichment is best-effort

        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(response);
        AdminAiTopUserDto top = Assert.Single(stats.AiSpend.TopUsers);
        Assert.Equal(userA, top.UserId);
        Assert.Equal(7_000L, top.CostMicroRub);
        Assert.Null(top.DisplayName);
        Assert.Null(top.AvatarUrl);
    }

    [Fact]
    public async Task Money_metrics_computed_from_usage_and_sessions()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();
        Guid userC = Guid.NewGuid();

        // total cost 60_000 micro over 3 distinct users; 2 OPEN_ANSWER_GRADE ops (30_000 of it).
        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 100, 50, 150, 25_000L, null);
        await SeedUsageAsync(userB, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 100, 50, 150, 5_000L, null);
        await SeedUsageAsync(userC, AiUsageOperation.MOCK_AGGREGATE, "gpt-4.1", 100, 100, 200, 30_000L, null);

        // 4 sessions started in the window.
        await SeedSessionAsync(userA, TrainingMode.DRILL, complete: false);
        await SeedSessionAsync(userA, TrainingMode.LEARN, complete: false);
        await SeedSessionAsync(userB, TrainingMode.MOCK, complete: false);
        await SeedSessionAsync(userC, TrainingMode.DRILL, complete: false);

        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        Assert.Equal(60_000L, stats.AiSpend.TotalCostMicroRub);

        AdminAiMoneyMetricsDto money = stats.AiSpend.Money;
        Assert.Equal(20_000L, money.CostPerUserMicroRub);    // 60_000 / 3 distinct users
        Assert.Equal(0.02m, money.CostPerUserRub);
        Assert.Equal(15_000L, money.CostPerSessionMicroRub); // 60_000 / 4 started sessions
        Assert.Equal(0.015m, money.CostPerSessionRub);
        Assert.Equal(30_000L, money.CostPerGradeMicroRub);   // 60_000 / 2 OPEN_ANSWER_GRADE ops
        Assert.Equal(0.03m, money.CostPerGradeRub);
        Assert.Equal(60_000L, money.ProjectedMonthMicroRub); // 60_000 / 30 days * 30
        Assert.Equal(0.06m, money.ProjectedMonthRub);
    }

    [Fact]
    public async Task Month_projection_extrapolates_window_daily_average()
    {
        Guid userA = Guid.NewGuid();
        await SeedUsageAsync(userA, AiUsageOperation.OPEN_ANSWER_GRADE, "gpt-4.1-mini", 10, 10, 20, 60_000L, null);

        AuthenticateAsAdmin();

        // 30-day window: 60_000 / 30 * 30 = 60_000.
        AdminStatsDto d30 = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));
        Assert.Equal(60_000L, d30.AiSpend.Money.ProjectedMonthMicroRub);

        // 10-day window, same single row still in window: 60_000 / 10 * 30 = 180_000.
        AdminStatsDto d10 = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=10"));
        Assert.Equal(180_000L, d10.AiSpend.Money.ProjectedMonthMicroRub);
    }

    [Fact]
    public async Task Money_metrics_are_zero_with_no_usage_or_sessions()
    {
        AuthenticateAsAdmin();
        AdminStatsDto stats = await ReadResultAsync<AdminStatsDto>(
            await Client.GetAsync("/trainer/admin/stats?days=30"));

        AdminAiMoneyMetricsDto money = stats.AiSpend.Money;
        Assert.Equal(0L, money.CostPerUserMicroRub);
        Assert.Equal(0L, money.CostPerSessionMicroRub);
        Assert.Equal(0L, money.CostPerGradeMicroRub);
        Assert.Equal(0L, money.ProjectedMonthMicroRub);
        Assert.Equal(0m, money.CostPerUserRub);
    }

    // --- helpers ---

    private async Task<Guid> SeedUsageAsync(
        Guid userId,
        AiUsageOperation operation,
        string model,
        int? inputTokens,
        int? outputTokens,
        int? totalTokens,
        long costMicroRub,
        Guid? sessionId) =>
        await ExecuteInDbAsync(async db =>
        {
            AiUsageRecord record = AiUsageRecord.Create(
                userId, operation, model, inputTokens, outputTokens, totalTokens, costMicroRub, sessionId);
            db.AiUsageRecords.Add(record);
            await db.SaveChangesAsync();
            return record.Id;
        });

    private async Task BackdateUsageAsync(Guid usageId, DateTimeOffset createdAt) =>
        await ExecuteInDbAsync(db =>
            db.Database.ExecuteSqlAsync(
                $"UPDATE trainer.ai_usage SET created_at = {createdAt} WHERE id = {usageId}"));

    private async Task SeedSessionAsync(Guid userId, TrainingMode mode, bool complete) =>
        await ExecuteInDbAsync(async db =>
        {
            TrainingSession session = TrainingSession.Create(
                userId, mode, trackId: null, [Guid.NewGuid()],
                revealPolicy: RevealPolicy.PER_QUESTION).Value;
            db.TrainingSessions.Add(session);
            await db.SaveChangesAsync();

            if (complete)
            {
                session.Complete(100);
                await db.SaveChangesAsync();
            }

            return session.Id;
        });
}
