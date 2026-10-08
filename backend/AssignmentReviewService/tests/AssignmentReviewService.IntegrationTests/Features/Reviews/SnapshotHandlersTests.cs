using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Wolverine.Tracking;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Phase 7 (#15): snapshot consumers `StoreIssueReviewSpecHandler` +
///     `StoreProjectGuidelinesHandler` — добавлены тесты в Phase 13+ ревью
///     (originally без coverage).
/// </summary>
public sealed class SnapshotHandlersTests : AssignmentReviewServiceTestsBase
{
    public SnapshotHandlersTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task IssueReviewSpec_FirstEvent_PersistsRow()
    {
        Guid issueId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new ReviewSpecUpdated(
            IssueId: issueId,
            ProjectId: projectId,
            AuthorId: authorId,
            AuthorPrompt: "focus on null safety",
            ReviewAspects: "check error handling"));

        await ExecuteInDbAsync(async db =>
        {
            IssueReviewSpec? spec = await db.IssueReviewSpecs.FirstOrDefaultAsync(s => s.IssueId == issueId);
            Assert.NotNull(spec);
            Assert.Equal(projectId, spec!.ProjectId);
            Assert.Equal(authorId, spec.AuthorId);
            Assert.Equal("focus on null safety", spec.AuthorPrompt);
            Assert.Equal("check error handling", spec.ReviewAspects);
        });
    }

    [Fact]
    public async Task IssueReviewSpec_SecondEvent_UpdatesExistingRow()
    {
        Guid issueId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new ReviewSpecUpdated(
            IssueId: issueId,
            ProjectId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            AuthorPrompt: "v1",
            ReviewAspects: null));

        Guid newProjectId = Guid.NewGuid();
        await host.InvokeMessageAndWaitAsync(new ReviewSpecUpdated(
            IssueId: issueId,
            ProjectId: newProjectId,
            AuthorId: Guid.NewGuid(),
            AuthorPrompt: "v2 updated",
            ReviewAspects: "new aspects"));

        await ExecuteInDbAsync(async db =>
        {
            // No duplicate row — same issue_id (unique constraint).
            int count = await db.IssueReviewSpecs.CountAsync(s => s.IssueId == issueId);
            Assert.Equal(1, count);

            IssueReviewSpec spec = await db.IssueReviewSpecs.FirstAsync(s => s.IssueId == issueId);
            Assert.Equal("v2 updated", spec.AuthorPrompt);
            Assert.Equal("new aspects", spec.ReviewAspects);
            Assert.Equal(newProjectId, spec.ProjectId);
        });
    }

    [Fact]
    public async Task ProjectGuidelines_FirstEvent_PersistsRow()
    {
        Guid projectId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new ProjectReviewContextUpdated(
            ProjectId: projectId,
            AuthorId: authorId,
            GuidelinesMarkdown: "# Guidelines\n\nAll projects must include tests."));

        await ExecuteInDbAsync(async db =>
        {
            ProjectReviewGuidelines? g = await db.ProjectReviewGuidelines
                .FirstOrDefaultAsync(x => x.ProjectId == projectId);
            Assert.NotNull(g);
            Assert.Equal(authorId, g!.AuthorId);
            Assert.Contains("must include tests", g.GuidelinesMarkdown, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ProjectGuidelines_SecondEvent_UpdatesExistingRow()
    {
        Guid projectId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        await host.InvokeMessageAndWaitAsync(new ProjectReviewContextUpdated(
            projectId, Guid.NewGuid(), "v1 guidelines"));
        await host.InvokeMessageAndWaitAsync(new ProjectReviewContextUpdated(
            projectId, Guid.NewGuid(), "v2 guidelines"));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.ProjectReviewGuidelines.CountAsync(g => g.ProjectId == projectId);
            Assert.Equal(1, count);
            ProjectReviewGuidelines g = await db.ProjectReviewGuidelines
                .FirstAsync(x => x.ProjectId == projectId);
            Assert.Equal("v2 guidelines", g.GuidelinesMarkdown);
        });
    }
}
