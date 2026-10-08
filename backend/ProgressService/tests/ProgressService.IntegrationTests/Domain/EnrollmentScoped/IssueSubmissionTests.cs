using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.IssueSubmissions;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class IssueSubmissionTests
{
    [Fact]
    public void ReviewLifecycle_Works()
    {
        IssueSubmission submission = CreateSubmission();

        var startReview = submission.StartReview(Guid.NewGuid());
        var approve = submission.Approve(IssueReviewFeedback.Create("Looks good").Value);

        Assert.True(startReview.IsSuccess);
        Assert.True(approve.IsSuccess);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.NotNull(submission.ReviewStartedAt);
        Assert.NotNull(submission.ReviewedAt);
    }

    [Fact]
    public void RequestChanges_WithoutStartReview_Fails()
    {
        IssueSubmission submission = CreateSubmission();
        IssueReviewFeedback feedback = IssueReviewFeedback.Create("Fix this").Value;

        var result = submission.RequestChanges(feedback);

        Assert.True(result.IsFailure);
        Assert.Equal(IssueSubmissionReviewStatus.PENDING, submission.ReviewStatus);
    }

    [Fact]
    public void Finalize_WhenAiGated_RaisesAwaitingManualReviewEvent()
    {
        // autoFinalize:false → ReadyForHumanReview=false (AI-gated, hidden from author inbox).
        // Finalize re-opens the gate and must raise the re-notify event so the author learns
        // the submission is back on their desk (#334).
        IssueSubmission submission = CreateSubmission(autoFinalize: false);

        var result = submission.Finalize();

        Assert.True(result.IsSuccess);
        Assert.True(submission.ReadyForHumanReview);
        Assert.Single(submission.DomainEvents.OfType<IssueSubmissionAwaitingManualReviewEvent>());
    }

    [Fact]
    public void Finalize_WhenAlreadyOpen_DoesNotRaiseEvent()
    {
        // autoFinalize:true → ReadyForHumanReview=true (never gated). Finalize is an idempotent
        // flip; the author already got the create-time notification, so no duplicate re-notify.
        IssueSubmission submission = CreateSubmission(autoFinalize: true);

        var result = submission.Finalize();

        Assert.True(result.IsSuccess);
        Assert.Empty(submission.DomainEvents.OfType<IssueSubmissionAwaitingManualReviewEvent>());
    }

    private static IssueSubmission CreateSubmission(bool autoFinalize = true)
    {
        IssueSubmissionPayload payload = IssueSubmissionPayload.Create("https://github.com/example/repo/pull/1").Value;
        AttemptNumber attemptNumber = AttemptNumber.Create(1).Value;
        return IssueSubmission.Create(Guid.NewGuid(), attemptNumber, payload, autoFinalize).Value;
    }
}
