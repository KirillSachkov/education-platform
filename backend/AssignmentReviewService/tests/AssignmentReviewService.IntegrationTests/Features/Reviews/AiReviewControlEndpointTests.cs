using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

public sealed class AiReviewControlEndpointTests : AssignmentReviewServiceTestsBase
{
    public AiReviewControlEndpointTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.AiClient.Reset();
        Factory.VcsProvider.Reset();
    }

    [Fact]
    public async Task RestartEndpoint_AllowsRunningReview_ResetsToQueued_AndPublishesRunCommand()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(authorId: DefaultUserId);

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'RUNNING', updated_at = NOW() WHERE id = {0}",
                reviewId);
        });

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/restart/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Single();
        Assert.Equal(reviewId, published.AiReviewId);
        Assert.True(published.AllowOversizedDiff);
        Assert.True(published.ForceFresh);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.QUEUED, review.Status);
            Assert.Null(review.LatestVerdict);
        });
    }

    [Fact]
    public async Task RestartEndpoint_InvalidatesExistingRunningLease()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(authorId: DefaultUserId);

        DateTimeOffset leaseAcquiredAt;
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IAiReviewsRepository reviews = scope.ServiceProvider.GetRequiredService<IAiReviewsRepository>();
            leaseAcquiredAt = (await reviews.TryAcquireRunningLockAsync(reviewId, CancellationToken.None))!.Value;
        }

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/restart/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IAiReviewsRepository reviews = scope.ServiceProvider.GetRequiredService<IAiReviewsRepository>();
            Assert.False(await reviews.IsRunningLeaseCurrentAsync(reviewId, leaseAcquiredAt, CancellationToken.None));
        }
    }

    [Fact]
    public async Task RestartEndpoint_ForceFreshRunsNewIteration_EvenWhenCommitWasAlreadyReviewed()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(authorId: DefaultUserId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "First pass.");
        await PostRunIterationAsync(reviewId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/restart/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Last();

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Second pass.");
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(published);
        }

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);

            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Equal(2, review.IterationsCount);
            Assert.Equal(2, review.Iterations.Count);
        });

        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task CancelEndpoint_StopsActiveReview_WithFailedCancelledIteration()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid reviewId = await SeedReviewAsync(authorId: DefaultUserId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/cancel/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);

            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.FAILED, iteration.Status);
            Assert.Equal("review.cancelled", iteration.FailureReason);
            Assert.Equal(iteration.Id, review.LatestIterationId);
        });
    }

    [Fact]
    public async Task AdminCancelActiveEndpoint_StopsOnlyQueuedAndRunningReviews()
    {
        AuthenticateAs("platform-admin", DefaultUserId);
        Guid queuedId = await SeedReviewAsync(authorId: DefaultUserId);
        Guid runningId = await SeedReviewAsync(authorId: DefaultUserId);
        Guid readyId = await SeedReviewAsync(authorId: DefaultUserId);

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'RUNNING', updated_at = NOW() WHERE id = {0}",
                runningId);
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE assignment_review.ai_reviews SET status = 'READY', updated_at = NOW() WHERE id = {0}",
                readyId);
        });

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/assignment-review/admin/reviews/active/cancel/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Dictionary<Guid, AiReview> reviews = await db.AiReviews
                .Include(r => r.Iterations)
                .Where(r => r.Id == queuedId || r.Id == runningId || r.Id == readyId)
                .ToDictionaryAsync(r => r.Id);

            Assert.Equal(AiReviewStatus.FAILED, reviews[queuedId].Status);
            Assert.Equal("review.cancelled", reviews[queuedId].Iterations.Single().FailureReason);
            Assert.Equal(AiReviewStatus.FAILED, reviews[runningId].Status);
            Assert.Equal("review.cancelled", reviews[runningId].Iterations.Single().FailureReason);
            Assert.Equal(AiReviewStatus.READY, reviews[readyId].Status);
            Assert.Empty(reviews[readyId].Iterations);
        });
    }

    private async Task<Guid> SeedReviewAsync(Guid authorId)
    {
        Guid reviewId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = AiReview.Create(
                submissionId: Guid.NewGuid(),
                issueId: Guid.NewGuid(),
                userId: Guid.NewGuid(),
                authorId: authorId,
                provider: VcsProvider.GITHUB,
                repoFullName: "test-org/student-pr",
                pullNumber: 7,
                pullRequestUrl: "https://github.com/test-org/student-pr/pull/7");
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
