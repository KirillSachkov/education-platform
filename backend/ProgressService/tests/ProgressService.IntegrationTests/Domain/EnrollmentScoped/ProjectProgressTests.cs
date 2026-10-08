using ProgressService.Domain.Projects;
using ProgressService.Domain.Projects.Events;

namespace ProgressService.IntegrationTests.Domain.EnrollmentScoped;

public class ProjectProgressTests
{
    [Fact]
    public void RegisterIssueCompleted_SetsInProgress()
    {
        ProjectProgress progress = ProjectProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;

        var result = progress.RegisterIssueCompleted();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectProgressStatus.IN_PROGRESS, progress.Status);
        Assert.Equal(1, progress.TotalIssuesCompleted);
    }

    [Fact]
    public void TryCompleteProject_WhenAllIssuesCompleted_Succeeds()
    {
        ProjectProgress progress = ProjectProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;
        progress.RegisterIssueCompleted();
        progress.RegisterIssueCompleted();

        var result = progress.TryCompleteProject();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectProgressStatus.COMPLETED, progress.Status);
        Assert.NotNull(progress.CompletedAt);
        Assert.Single(progress.DomainEvents);
        Assert.IsType<ProjectProgressCompletedEvent>(progress.DomainEvents[0]);
    }

    [Fact]
    public void TryCompleteProject_BeforeAllIssuesCompleted_DoesNothing()
    {
        ProjectProgress progress = ProjectProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 2).Value;
        progress.RegisterIssueCompleted();

        var result = progress.TryCompleteProject();

        Assert.True(result.IsSuccess);
        Assert.Equal(ProjectProgressStatus.IN_PROGRESS, progress.Status);
        Assert.Null(progress.CompletedAt);
    }

    [Fact]
    public void TryCompleteProject_WhenCalledAfterCompletion_DoesNotRaiseSecondEvent()
    {
        ProjectProgress progress = ProjectProgress.Create(Guid.NewGuid(), Guid.NewGuid(), 1).Value;
        progress.RegisterIssueCompleted();
        progress.TryCompleteProject();

        var result = progress.TryCompleteProject();

        Assert.True(result.IsSuccess);
        Assert.Single(progress.DomainEvents);
    }
}
