using ProgressService.Domain.Issues;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class IssueProgressTests
{
    [Fact]
    public void ValidIssueFlow_ReachesCompleted()
    {
        IssueProgress progress = IssueProgress.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var start = progress.StartWork();
        var submit = progress.SubmitForReview();
        var changes = progress.RequestChanges();
        var resubmit = progress.SubmitForReview();
        var approve = progress.Approve();

        Assert.True(start.IsSuccess);
        Assert.True(submit.IsSuccess);
        Assert.True(changes.IsSuccess);
        Assert.True(resubmit.IsSuccess);
        Assert.True(approve.IsSuccess);
        Assert.Equal(IssueProgressStatus.COMPLETED, progress.Status);
        Assert.NotNull(progress.CompletedAt);
    }

    [Fact]
    public void InvalidTransition_Fails()
    {
        IssueProgress progress = IssueProgress.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()).Value;

        var result = progress.Approve();

        Assert.True(result.IsFailure);
        Assert.Equal(IssueProgressStatus.NOT_STARTED, progress.Status);
    }
}
