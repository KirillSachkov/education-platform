using System.Diagnostics.Metrics;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Diagnostics;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Core.Maintenance;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Issue #690 — faster stuck-review recovery (StaleReviewMaxAgeMinutes 60→15) made safe
///     for legitimately long multi-batch reviews via a liveness heartbeat:
///     1. The periodic watchdog measures RUNNING staleness by GREATEST(updated_at, heartbeat_at),
///        so a review that keeps heart-beating is never mistaken for stuck (anti-starvation).
///     2. A review with no progress past the threshold is requeued + RunAiReviewRequested republished.
///     3. The watchdog feeds the `assignment_review_stuck_reviews` gauge each periodic tick.
///     4. Multi-batch reviews bump heartbeat_at after every batch; single-batch ones do not.
/// </summary>
public sealed class StaleReviewHeartbeatTests : AssignmentReviewServiceTestsBase
{
    public StaleReviewHeartbeatTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.AiClient.Reset();
        Factory.VcsProvider.Reset();
    }

    // --- Periodic watchdog: GREATEST(updated_at, heartbeat_at) staleness ---

    [Fact]
    public async Task StaleRecovery_RequeuesRunningReview_WhenNoHeartbeatAndUpdatedAtOld()
    {
        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 20, heartbeatMinutesAgo: null);

        await InvokeStaleRecoveryAsync();

        Assert.Equal(reviewId, OutboxCollector.OfType<RunAiReviewRequested>().Single().AiReviewId);
        await AssertStatusAsync(reviewId, AiReviewStatus.QUEUED);
    }

    [Fact]
    public async Task StaleRecovery_SkipsRunningReview_WhenHeartbeatIsFresh()
    {
        // updated_at is old (lock acquired 20m ago), but the review is still progressing —
        // a batch heart-beat landed 2m ago. GREATEST keeps it out of the stale set.
        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 20, heartbeatMinutesAgo: 2);

        await InvokeStaleRecoveryAsync();

        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
        await AssertStatusAsync(reviewId, AiReviewStatus.RUNNING);
    }

    [Fact]
    public async Task StaleRecovery_RequeuesRunningReview_WhenHeartbeatAlsoStale()
    {
        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 20, heartbeatMinutesAgo: 20);

        await InvokeStaleRecoveryAsync();

        Assert.Equal(reviewId, OutboxCollector.OfType<RunAiReviewRequested>().Single().AiReviewId);
        await AssertStatusAsync(reviewId, AiReviewStatus.QUEUED);
    }

    [Fact]
    public async Task StaleRecovery_SkipsFreshRunningReview()
    {
        // 15-minute threshold default: a review running for 5m is not stale.
        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 5, heartbeatMinutesAgo: null);

        await InvokeStaleRecoveryAsync();

        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
        await AssertStatusAsync(reviewId, AiReviewStatus.RUNNING);
    }

    [Fact]
    public async Task StaleRecovery_RequeuesOldQueuedReview()
    {
        Guid reviewId = await SeedReviewAsync();
        await ExecuteInDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE assignment_review.ai_reviews SET status='QUEUED', updated_at=NOW() - INTERVAL '20 minutes' WHERE id={0}",
            reviewId));

        await InvokeStaleRecoveryAsync();

        Assert.Equal(reviewId, OutboxCollector.OfType<RunAiReviewRequested>().Single().AiReviewId);
        await AssertStatusAsync(reviewId, AiReviewStatus.QUEUED);
    }

    [Fact]
    public async Task StaleRecovery_SkipsFreshQueuedReview()
    {
        // Freshly QUEUED (e.g. just created) must NOT be requeued — its run command is in flight.
        Guid reviewId = await SeedReviewAsync();
        await ExecuteInDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE assignment_review.ai_reviews SET status='QUEUED', updated_at=NOW() - INTERVAL '2 minutes' WHERE id={0}",
            reviewId));

        await InvokeStaleRecoveryAsync();

        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    // --- Stuck-review gauge (#690) ---

    [Fact]
    public async Task StaleRecovery_FeedsStuckGauge_WithRequeuedCount()
    {
        Guid r1 = await SeedReviewAsync();
        Guid r2 = await SeedReviewAsync();
        await SetRunningAsync(r1, updatedAtMinutesAgo: 30, heartbeatMinutesAgo: null);
        await SetRunningAsync(r2, updatedAtMinutesAgo: 30, heartbeatMinutesAgo: null);

        await InvokeStaleRecoveryAsync();

        Assert.Equal(2, ObserveStuckReviewGauge());
    }

    [Fact]
    public async Task StaleRecovery_FeedsStuckGauge_Zero_WhenNothingStale()
    {
        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 1, heartbeatMinutesAgo: null);

        await InvokeStaleRecoveryAsync();

        Assert.Equal(0, ObserveStuckReviewGauge());
    }

    [Fact]
    public async Task StartupRecovery_DoesNotFeedStuckGauge()
    {
        // Startup recovery resets ALL RUNNING (restart-recovery, not "stuck") — it must NOT
        // touch the gauge, else every redeploy would spike a false stuck-review alert.
        // Prime the gauge to a known 0 via a clean periodic sweep, then prove startup leaves it.
        await InvokeStaleRecoveryAsync();
        Assert.Equal(0, ObserveStuckReviewGauge());

        Guid reviewId = await SeedReviewAsync();
        await SetRunningAsync(reviewId, updatedAtMinutesAgo: 30, heartbeatMinutesAgo: null);

        await InvokeStartupRecoveryAsync();

        // Review WAS recovered (RUNNING → QUEUED), but the gauge stayed 0 — startup skipped it.
        await AssertStatusAsync(reviewId, AiReviewStatus.QUEUED);
        Assert.Equal(0, ObserveStuckReviewGauge());
    }

    // --- Heartbeat repository method ---

    [Fact]
    public async Task HeartbeatRunning_SetsHeartbeatAt_AndPreservesLease()
    {
        // Central invariant of the whole design: the heartbeat records progress WITHOUT
        // touching updated_at (the lease fencing token), so the still-running iteration
        // keeps its lease and no concurrent run / duplicate GitHub post can be triggered.
        Guid reviewId = await SeedReviewAsync();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IAiReviewsRepository repo = scope.ServiceProvider.GetRequiredService<IAiReviewsRepository>();

        // Acquire the running lease the same way the handler does (sets status=RUNNING,
        // updated_at = acquiredAt — the fencing token).
        DateTimeOffset? acquiredAt = await repo.TryAcquireRunningLockAsync(reviewId);
        Assert.NotNull(acquiredAt);

        await repo.HeartbeatRunningAsync(reviewId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.NotNull(review.HeartbeatAt);
        });

        // Lease still current AFTER the heartbeat → updated_at was NOT changed (this is the
        // exact `updated_at = @AcquiredAt` check the running iteration relies on).
        Assert.True(await repo.IsRunningLeaseCurrentAsync(reviewId, acquiredAt!.Value));
    }

    [Fact]
    public async Task HeartbeatRunning_IsNoOp_WhenReviewIsNotRunning()
    {
        // Default seeded review is QUEUED — heartbeat must not touch a non-RUNNING row
        // (avoids resurrecting a review the watchdog already requeued / superseded).
        Guid reviewId = await SeedReviewAsync();

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IAiReviewsRepository repo = scope.ServiceProvider.GetRequiredService<IAiReviewsRepository>();
            await repo.HeartbeatRunningAsync(reviewId);
        }

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Null(review.HeartbeatAt);
        });
    }

    // --- End-to-end: multi-batch review heart-beats, single-batch does not ---

    [Fact]
    public async Task MultiBatchReview_BumpsHeartbeatAt()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(DefaultUserId);
        await SeedInstallationAsync("test-org");

        // Two reviewable source files, 1000 additions each → DiffChunker splits into 2 batches
        // (default per-batch limit 1500). Each batch → one LLM call → one heartbeat.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) => Task.FromResult(
            Result.Success<VcsDiff, Error>(new VcsDiff(
                HeadSha: "deadbeef",
                Files:
                [
                    new VcsDiffFile("src/A.cs", null, "modified", 1000, 0, "@@ +1 @@\n+a", []),
                    new VcsDiffFile("src/B.cs", null, "modified", 1000, 0, "@@ +1 @@\n+b", []),
                ],
                TotalAdditions: 2000,
                TotalDeletions: 0)));
        // FakeAiClient default LOOKS_GOOD per call.

        await PostRunIterationAsync(reviewId);

        // Two LLM calls confirm the multi-batch path actually ran.
        Assert.True(Factory.AiClient.ReceivedRequests.Count >= 2,
            $"expected >=2 LLM calls (multi-batch), got {Factory.AiClient.ReceivedRequests.Count}");

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.NotNull(review.HeartbeatAt);
        });
    }

    [Fact]
    public async Task SingleBatchReview_LeavesHeartbeatNull()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(DefaultUserId);
        await SeedInstallationAsync("test-org");
        // Default DiffHandler returns a tiny single-file diff → single-batch fast path → no heartbeat.
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "All good.");

        await PostRunIterationAsync(reviewId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Null(review.HeartbeatAt);
        });
    }

    // --- Helpers ---

    private async Task InvokeStaleRecoveryAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        StaleRunningReviewRecoveryService recovery =
            ActivatorUtilities.CreateInstance<StaleRunningReviewRecoveryService>(scope.ServiceProvider);
        await recovery.RecoverStaleAsync(CancellationToken.None);
    }

    private async Task InvokeStartupRecoveryAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        StaleRunningReviewRecoveryService recovery =
            ActivatorUtilities.CreateInstance<StaleRunningReviewRecoveryService>(scope.ServiceProvider);
        await recovery.RecoverStartupAsync(CancellationToken.None);
    }

    private long ObserveStuckReviewGauge()
    {
        // Force the metrics singleton to exist (it is created at host start via the hosted service).
        _ = Services.GetRequiredService<AssignmentReviewMetrics>();

        long value = -1;
        using MeterListener listener = new();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Name, "assignment_review_stuck_reviews", StringComparison.Ordinal))
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, measurement, _, _) => value = measurement);
        listener.Start();
        listener.RecordObservableInstruments();
        // Fail fast if the instrument was never found (e.g. metrics lifetime drift) rather than
        // silently comparing against the -1 sentinel and masking a broken test.
        Assert.NotEqual(-1L, value);
        return value;
    }

    private async Task SetRunningAsync(Guid reviewId, int updatedAtMinutesAgo, int? heartbeatMinutesAgo)
    {
        // Parameterized make_interval(mins => N) keeps the analyzer happy (EF1003 — no
        // string concatenation into raw SQL) while still varying the ages per test.
        if (heartbeatMinutesAgo is null)
        {
            await ExecuteInDbAsync(db => db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status='RUNNING', " +
                "updated_at = NOW() - make_interval(mins => {1}), heartbeat_at = NULL WHERE id = {0}",
                reviewId, updatedAtMinutesAgo));
            return;
        }

        await ExecuteInDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "UPDATE assignment_review.ai_reviews SET status='RUNNING', " +
            "updated_at = NOW() - make_interval(mins => {1}), " +
            "heartbeat_at = NOW() - make_interval(mins => {2}) WHERE id = {0}",
            reviewId, updatedAtMinutesAgo, heartbeatMinutesAgo.Value));
    }

    private async Task AssertStatusAsync(Guid reviewId, AiReviewStatus expected)
    {
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(expected, review.Status);
        });
    }

    private async Task<Guid> SeedReviewAsync(Guid? userId = null)
    {
        Guid uid = userId ?? Guid.NewGuid();
        Guid reviewId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = AiReview.Create(
                submissionId: Guid.NewGuid(),
                issueId: Guid.NewGuid(),
                userId: uid,
                authorId: Guid.NewGuid(),
                provider: VcsProvider.GITHUB,
                repoFullName: "test-org/test-repo",
                pullNumber: 1,
                pullRequestUrl: "https://github.com/test-org/test-repo/pull/1");
            db.Entry(review).Property("Id").CurrentValue = reviewId;
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return reviewId;
    }

    private async Task SeedInstallationAsync(string ownerLogin)
    {
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation installation = VcsInstallation.Create(
                VcsProvider.GITHUB,
                installationId: "999",
                ownerType: VcsInstallationOwnerType.ORG,
                ownerLogin: ownerLogin,
                ownerExternalId: "555",
                linkedUserId: DefaultUserId,
                repoSelections: RepoSelections.AllRepos());
            db.VcsInstallations.Add(installation);
            await db.SaveChangesAsync();
        });
    }
}
