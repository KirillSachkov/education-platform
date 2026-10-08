using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
using Core.Database;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;
using SharedKernel.Exceptions;

namespace AssignmentReviewService.UnitTests.Reviews;

public sealed class SnapshotHandlerReliabilityTests
{
    [Fact]
    public async Task Review_spec_save_failure_should_escape_for_wolverine_retry()
    {
        IIssueReviewSpecsRepository specs = Substitute.For<IIssueReviewSpecsRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        specs.GetByAsync(
                Arg.Any<Expression<Func<IssueReviewSpec, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((IssueReviewSpec?)null);
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());
        var sut = new StoreIssueReviewSpecHandler(
            specs,
            transactions,
            Substitute.For<ILogger<StoreIssueReviewSpecHandler>>());

        await Assert.ThrowsAsync<TransientException>(() => sut.HandleAsync(
            new ReviewSpecUpdated(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "prompt",
                "aspects"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Project_guidelines_save_failure_should_escape_for_wolverine_retry()
    {
        IProjectReviewGuidelinesRepository guidelines =
            Substitute.For<IProjectReviewGuidelinesRepository>();
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        guidelines.GetByAsync(
                Arg.Any<Expression<Func<ProjectReviewGuidelines, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns((ProjectReviewGuidelines?)null);
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(GeneralErrors.DatabaseError());
        var sut = new StoreProjectGuidelinesHandler(
            guidelines,
            transactions,
            Substitute.For<ILogger<StoreProjectGuidelinesHandler>>());

        await Assert.ThrowsAsync<TransientException>(() => sut.HandleAsync(
            new ProjectReviewContextUpdated(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                "guidelines"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Hard_delete_should_execute_each_direct_delete_once()
    {
        IAiReviewsRepository reviews = Substitute.For<IAiReviewsRepository>();
        IIssueReviewSpecsRepository specs = Substitute.For<IIssueReviewSpecsRepository>();
        reviews.DeleteByIssueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(2);
        specs.DeleteByIssueIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(1);
        var sut = new IssueHardDeletedAssignmentReviewHandler(
            reviews,
            specs,
            Substitute.For<ILogger<IssueHardDeletedAssignmentReviewHandler>>());

        await sut.HandleAsync(new IssueHardDeleted(Guid.CreateVersion7()), CancellationToken.None);

        await reviews.Received(1).DeleteByIssueIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await specs.Received(1).DeleteByIssueIdAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
