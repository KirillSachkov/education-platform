using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Wolverine.Tracking;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Phase 13+ (#15) cascade cleanup при <c>issue.hard_deleted</c>. Context-cleanup
///     удалён в #320 (RAG-pipeline выпилен) — handler чистит только
///     <c>ai_reviews</c> + <c>issue_review_specs</c>.
/// </summary>
public sealed class IssueHardDeletedCleanupTests : AssignmentReviewServiceTestsBase
{
    public IssueHardDeletedCleanupTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task IssueHardDeleted_CascadesAiReviews_AndSpecs()
    {
        Guid issueId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        await ExecuteInDbAsync(async db =>
        {
            // Seed AiReview + iteration
            AiReview review = AiReview.Create(
                submissionId: Guid.NewGuid(),
                issueId: issueId,
                userId: Guid.NewGuid(),
                authorId: Guid.NewGuid(),
                provider: VcsProvider.GITHUB,
                repoFullName: "owner/repo",
                pullNumber: 1,
                pullRequestUrl: "https://github.com/owner/repo/pull/1");
            db.AiReviews.Add(review);

            // Seed IssueReviewSpec
            IssueReviewSpec spec = IssueReviewSpec.Create(
                issueId: issueId,
                projectId: projectId,
                authorId: Guid.NewGuid(),
                authorPrompt: "test prompt",
                reviewAspects: null);
            db.IssueReviewSpecs.Add(spec);

            await db.SaveChangesAsync();
        });

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueHardDeleted(issueId));

        await ExecuteInDbAsync(async db =>
        {
            Assert.False(await db.AiReviews.AnyAsync(r => r.IssueId == issueId));
            Assert.False(await db.IssueReviewSpecs.AnyAsync(s => s.IssueId == issueId));
        });
    }

    [Fact]
    public async Task IssueHardDeleted_UnrelatedDataPreserved()
    {
        Guid issueToDelete = Guid.NewGuid();
        Guid otherIssue = Guid.NewGuid();

        await ExecuteInDbAsync(async db =>
        {
            AiReview r1 = AiReview.Create(
                Guid.NewGuid(), issueToDelete, Guid.NewGuid(), Guid.NewGuid(),
                VcsProvider.GITHUB, "owner/repo", 1, "url1");
            AiReview r2 = AiReview.Create(
                Guid.NewGuid(), otherIssue, Guid.NewGuid(), Guid.NewGuid(),
                VcsProvider.GITHUB, "owner/repo", 2, "url2");
            db.AiReviews.AddRange(r1, r2);
            await db.SaveChangesAsync();
        });

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueHardDeleted(issueToDelete));

        await ExecuteInDbAsync(async db =>
        {
            Assert.False(await db.AiReviews.AnyAsync(r => r.IssueId == issueToDelete));
            Assert.True(await db.AiReviews.AnyAsync(r => r.IssueId == otherIssue));
        });
    }

    [Fact]
    public async Task IssueHardDeleted_Idempotent_NoIssueAtAll()
    {
        // Issue never existed in ARS — handler must not throw.
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new IssueHardDeleted(Guid.NewGuid()));
        // No assertion — passing means no exception.
    }
}
