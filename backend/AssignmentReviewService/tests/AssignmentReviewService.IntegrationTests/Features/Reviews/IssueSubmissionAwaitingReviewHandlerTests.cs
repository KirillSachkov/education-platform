using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using Wolverine.Tracking;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Phase 8 (#15): ARS consumer для progress.events / issue_submission.awaiting_review.
///     Создаёт AiReview под submission, если payload — GitHub PR URL.
/// </summary>
public sealed class IssueSubmissionAwaitingReviewHandlerTests : AssignmentReviewServiceTestsBase
{
    public IssueSubmissionAwaitingReviewHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GithubPrUrl_WithInstallation_CreatesQueuedAiReview_AndPublishesGate()
    {
        Guid submissionId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        await SeedInstallationAsync("test-org");

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: userId,
            AuthorId: Guid.NewGuid(),
            IssueId: issueId,
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/test-org/student-pr/pull/42"));

        AiReviewQueuedForSubmission queued = OutboxCollector.OfType<AiReviewQueuedForSubmission>().Single();
        Assert.Equal(submissionId, queued.SubmissionId);
        Assert.Equal(userId, queued.UserId);

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.SubmissionId == submissionId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.QUEUED, review!.Status);
            Assert.Equal(42, review.PullNumber);
            Assert.Equal("test-org/student-pr", review.RepoFullName);
            Assert.Empty(review.Iterations);
        });
    }

    // #668 — PR URL с trailing slash / #fragment / ?query / sub-path должен распознаваться
    // (раньше строгий якорь `$` отбрасывал такие URL → AiReview не создавался, ученик завис).
    // pull_request_url хранится в каноничной форме (хвост отрезан).
    [Theory]
    [InlineData("https://github.com/test-org/student-pr/pull/42/")]
    [InlineData("https://github.com/test-org/student-pr/pull/42#pullrequestreview-4580609186")]
    [InlineData("https://github.com/test-org/student-pr/pull/42?diff=split")]
    [InlineData("https://github.com/test-org/student-pr/pull/42/files")]
    [InlineData("https://github.com/test-org/student-pr/pull/42/changes/426891aae3")]
    public async Task GithubPrUrl_WithTail_CreatesQueuedAiReview_WithCanonicalUrl(string payload)
    {
        Guid submissionId = Guid.NewGuid();
        await SeedInstallationAsync("test-org");

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: payload));

        Assert.Single(OutboxCollector.OfType<AiReviewQueuedForSubmission>());

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.SubmissionId == submissionId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.QUEUED, review!.Status);
            Assert.Equal(42, review.PullNumber);
            Assert.Equal("test-org/student-pr", review.RepoFullName);
            // Каноничная форма — хвост (slash/fragment/query/sub-path) отрезан.
            Assert.Equal("https://github.com/test-org/student-pr/pull/42", review.PullRequestUrl);
            Assert.Empty(review.Iterations);
        });
    }

    [Fact]
    public async Task GithubPrUrl_NoInstallation_CreatesFailedAiReview_NoGatePublished()
    {
        Guid submissionId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/no-installation-org/repo/pull/1"));

        Assert.Empty(OutboxCollector.OfType<AiReviewQueuedForSubmission>());

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.SubmissionId == submissionId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.FAILED, review!.Status);
            AiReviewIteration iteration = Assert.Single(review.Iterations);
            Assert.Equal("review.no_installation", iteration.FailureReason);
        });
    }

    [Fact]
    public async Task NonGithubPayload_DoesNotCreateAiReview()
    {
        Guid submissionId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://sachkov-learn.net/internal/123"));

        await ExecuteInDbAsync(async db =>
        {
            bool exists = await db.AiReviews.AnyAsync(r => r.SubmissionId == submissionId);
            Assert.False(exists);
        });
    }

    [Fact]
    public async Task AiReviewNotRequested_DoesNotCreateAiReview()
    {
        Guid submissionId = Guid.NewGuid();
        await SeedInstallationAsync("test-org");
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/test-org/student-pr/pull/42",
            AiReviewRequested: false));

        Assert.Empty(OutboxCollector.OfType<AiReviewQueuedForSubmission>());
        await ExecuteInDbAsync(async db =>
        {
            bool exists = await db.AiReviews.AnyAsync(r => r.SubmissionId == submissionId);
            Assert.False(exists);
        });
    }

    [Fact]
    public async Task DuplicateEvent_Idempotent_NoSecondAiReview()
    {
        Guid submissionId = Guid.NewGuid();
        await SeedInstallationAsync("test-org");
        IHost host = Services.GetRequiredService<IHost>();

        IssueSubmissionAwaitingReview message = new(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/test-org/repo/pull/7");

        await host.InvokeMessageAndWaitAsync(message);
        await host.InvokeMessageAndWaitAsync(message);

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.AiReviews.CountAsync(r => r.SubmissionId == submissionId);
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public async Task ReviewDisabled_DoesNotCreateAiReview_NoGatePublished()
    {
        // #355: master switch off → submission идёт сразу на ручное ревью, AiReview не создаётся.
        Guid submissionId = Guid.NewGuid();
        await SeedReviewDisabledAsync();
        await SeedInstallationAsync("test-org");

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueSubmissionAwaitingReview(
            SubmissionId: submissionId,
            StudentUserId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            SubmittedAt: DateTimeOffset.UtcNow,
            Payload: "https://github.com/test-org/student-pr/pull/42"));

        Assert.Empty(OutboxCollector.OfType<AiReviewQueuedForSubmission>());
        await ExecuteInDbAsync(async db =>
        {
            bool exists = await db.AiReviews.AnyAsync(r => r.SubmissionId == submissionId);
            Assert.False(exists);
        });
    }

    private async Task SeedReviewDisabledAsync()
    {
        await ExecuteInDbAsync(async db =>
        {
            AiModelSlot slot = AiModelSlot.Create("deepseek/deepseek-v4-pro", 0.2, 4000, 300).Value;
            AiModelSettings settings = AiModelSettings.Create(
                slot, Guid.NewGuid(), reviewerBasePrompt: null, reviewEnabled: false).Value;
            db.Set<AiModelSettings>().Add(settings);
            await db.SaveChangesAsync();
        });
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
                linkedUserId: AssignmentReviewServiceTestsBase.DefaultUserId,
                repoSelections: RepoSelections.AllRepos());
            db.VcsInstallations.Add(installation);
            await db.SaveChangesAsync();
        });
    }
}
