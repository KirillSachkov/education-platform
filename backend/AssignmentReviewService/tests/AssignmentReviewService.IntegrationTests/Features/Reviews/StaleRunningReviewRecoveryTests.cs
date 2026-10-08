using AssignmentReviewService.Core.Maintenance;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
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
///     Regression tests for issue #474:
///     1. Stale RUNNING review (process restarted after lock acquired, before SaveChanges)
///        → startup recovery resets to QUEUED.
///     2. LatestIterationId=Guid.Empty after successful iteration
///        (OnIterationCompleted called before EF ValueGenerator runs)
///        → two-SaveChanges fix ensures LatestIterationId = real GUID.
/// </summary>
public sealed class StaleRunningReviewRecoveryTests : AssignmentReviewServiceTestsBase
{
    public StaleRunningReviewRecoveryTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        // AiClient is not reset in ResetDatabaseAsync — reset explicitly per-test
        // (same pattern as RunIterationTests) to prevent sticky state bleed-over.
        Factory.AiClient.Reset();
    }

    // --- Bug 1: Stale RUNNING after restart ---

    [Fact]
    public async Task StaleRunningRecovery_ResetsToQueued_WhenNoIteration()
    {
        // Arrange: seed a review and simulate a stuck RUNNING state by directly
        // calling TryAcquireRunningLockAsync (as the handler would), but NOT
        // proceeding to StartIteration/SaveChanges (simulating mid-iteration crash).
        Guid reviewId = await SeedReviewAsync();

        await ExecuteInDbAsync(async db =>
        {
            // Direct DB update mimics TryAcquireRunningLockAsync raw-SQL — the
            // review is stuck RUNNING with no iteration and no Wolverine message.
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'RUNNING', updated_at = NOW() WHERE id = {0}",
                reviewId);
        });

        // Confirm stuck state
        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.RUNNING, review!.Status);
        });

        // Act: invoke the startup recovery service (same as on container restart)
        await InvokeRecoveryAsync();

        // Assert: review is now QUEUED — user can retry
        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.QUEUED, review!.Status);
            Assert.Empty(review.Iterations);
        });
    }

    [Fact]
    public async Task StaleRunningRecovery_ResetsMultipleReviews()
    {
        // Arrange: two stuck RUNNING reviews
        Guid reviewId1 = await SeedReviewAsync();
        Guid reviewId2 = await SeedReviewAsync();

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'RUNNING', updated_at = NOW() " +
                "WHERE id = ANY(ARRAY[{0}, {1}]::uuid[])",
                reviewId1, reviewId2);
        });

        // Act
        await InvokeRecoveryAsync();

        // Assert: both reset to QUEUED
        await ExecuteInDbAsync(async db =>
        {
            List<AiReview> reviews = await db.AiReviews
                .Where(r => r.Id == reviewId1 || r.Id == reviewId2)
                .ToListAsync();
            Assert.Equal(2, reviews.Count);
            Assert.All(reviews, r => Assert.Equal(AiReviewStatus.QUEUED, r.Status));
        });
    }

    [Fact]
    public async Task StaleRunningRecovery_LeavesReadyAndFailedReviewsUntouched()
    {
        // Arrange
        Guid runningId = await SeedReviewAsync();
        Guid readyId = await SeedReviewAsync();
        Guid failedId = await SeedReviewAsync();

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'RUNNING' WHERE id = {0}", runningId);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'READY' WHERE id = {0}", readyId);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'FAILED' WHERE id = {0}", failedId);
        });

        // Act
        await InvokeRecoveryAsync();

        // Assert: only RUNNING was reset
        await ExecuteInDbAsync(async db =>
        {
            AiReview? running = await db.AiReviews.FirstOrDefaultAsync(r => r.Id == runningId);
            AiReview? ready = await db.AiReviews.FirstOrDefaultAsync(r => r.Id == readyId);
            AiReview? failed = await db.AiReviews.FirstOrDefaultAsync(r => r.Id == failedId);

            Assert.Equal(AiReviewStatus.QUEUED, running!.Status);
            Assert.Equal(AiReviewStatus.READY, ready!.Status);
            Assert.Equal(AiReviewStatus.FAILED, failed!.Status);
        });
    }

    [Fact]
    public async Task StaleRecovery_RequeuesOldQueuedReview_WhenRunCommandWasLost()
    {
        Guid reviewId = await SeedReviewAsync();

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                UPDATE assignment_review.ai_reviews
                SET status = 'QUEUED', updated_at = NOW() - INTERVAL '2 hours'
                WHERE id = {0}
                """,
                reviewId);
        });

        await InvokeRecoveryAsync();

        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Single();
        Assert.Equal(reviewId, published.AiReviewId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.QUEUED, review.Status);
            Assert.True(review.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-10));
        });
    }

    // --- Bug 2: LatestIterationId=Guid.Empty regression ---

    [Fact]
    public async Task HappyPath_LatestIterationId_IsRealGuid_NotEmpty()
    {
        // Reproducer for #474: before the two-SaveChanges fix, AiReview.latest_iteration_id
        // was written as 00000000-0000-0000-0000-000000000000 because OnIterationCompleted
        // was called before EF ValueGenerator assigned the iteration's real Id.
        Guid userId = DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "All good.");

        await PostRunIterationAsync(reviewId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);

            // Diagnostic: show failure reason if status is not READY
            string? failureReason = review!.Iterations.FirstOrDefault()?.FailureReason;
            Assert.True(review.Status == AiReviewStatus.READY,
                $"Expected READY but got {review.Status}. FailureReason={failureReason ?? "<none>"}. IterationsCount={review.IterationsCount}");

            Assert.Single(review.Iterations);

            AiReviewIteration iteration = review.Iterations.Single();
            // Core regression assertion: LatestIterationId must be the REAL iteration GUID.
            Assert.NotEqual(Guid.Empty, review.LatestIterationId);
            Assert.Equal(iteration.Id, review.LatestIterationId);
            // Iteration itself must also have a non-empty ID.
            Assert.NotEqual(Guid.Empty, iteration.Id);
        });
    }

    [Fact]
    public async Task HappyPath_AiReviewIterationCompleted_HasRealIterationId()
    {
        // Regression: AiReviewIterationCompleted.IterationId was Guid.Empty before #474
        // because it was captured before EF ValueGenerator ran.
        Guid userId = DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Fix naming.", ("src/Foo.cs", 5, "rename"));

        await PostRunIterationAsync(reviewId);

        AiReviewIterationCompleted? evt = OutboxCollector
            .OfType<AiReviewIterationCompleted>()
            .FirstOrDefault();
        Assert.NotNull(evt);
        Assert.NotEqual(Guid.Empty, evt!.IterationId);

        // Outbox IterationId must match the persisted iteration Id
        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.Equal(review!.Iterations.Single().Id, evt.IterationId);
        });
    }

    [Fact]
    public async Task FailedIteration_LatestIterationId_IsRealGuid_NotEmpty()
    {
        // Same regression but for the FAILED path (PersistFailedIterationAsync).
        Guid userId = DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        Factory.AiClient.QueueFailureSticky(
            Error.Failure("ai.upstream.unavailable", "polza 503"));

        await PostRunIterationAsync(reviewId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.FAILED, review!.Status);

            AiReviewIteration iteration = review.Iterations.Single();
            Assert.NotEqual(Guid.Empty, review.LatestIterationId);
            Assert.Equal(iteration.Id, review.LatestIterationId);
        });
    }

    // --- Recovery via idempotency check repair ---

    [Fact]
    public async Task IdempotencyReplay_RepairsStaleLatestIterationId()
    {
        // Arrange: simulate pre-#474 state where an iteration exists with a real GUID
        // but the review has LatestIterationId=Guid.Empty (old bug residue).
        // On retry with the same sha, the idempotency check should repair the review state.
        Guid userId = DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "All fine.");

        // First run — completes normally
        await PostRunIterationAsync(reviewId);

        // Manually corrupt LatestIterationId to simulate pre-#474 state
        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET latest_iteration_id = '00000000-0000-0000-0000-000000000000', status = 'QUEUED' WHERE id = {0}",
                reviewId);
        });

        // Confirm corrupted state
        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.QUEUED, review!.Status);
            Assert.Equal(Guid.Empty, review.LatestIterationId);
        });

        OutboxCollector.Clear();

        // Act: retry with same sha — idempotency check should detect and repair
        await PostRunIterationAsync(reviewId);

        // Assert: review is now READY with correct LatestIterationId
        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review!.Status);
            Assert.NotEqual(Guid.Empty, review.LatestIterationId);
            // Only one iteration — carry-forward wasn't triggered, idempotency matched
            Assert.Single(review.Iterations);
            Assert.Equal(review.Iterations.Single().Id, review.LatestIterationId);
        });

        // AiReviewIterationCompleted should be published again (repair re-published it)
        Assert.NotEmpty(OutboxCollector.OfType<AiReviewIterationCompleted>());
    }

    // --- Helpers ---

    /// <summary>
    ///     Creates a fresh <see cref="StaleRunningReviewRecoveryService"/> via ActivatorUtilities
    ///     (AddHostedService registers as IHostedService, not as the concrete type) and invokes
    ///     its startup recovery path — same recovery logic as a real container restart,
    ///     without starting the periodic hosted-service loop in tests.
    /// </summary>
    private async Task InvokeRecoveryAsync()
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        StaleRunningReviewRecoveryService recovery =
            ActivatorUtilities.CreateInstance<StaleRunningReviewRecoveryService>(
                scope.ServiceProvider);
        await recovery.RecoverStartupAsync(CancellationToken.None);
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
