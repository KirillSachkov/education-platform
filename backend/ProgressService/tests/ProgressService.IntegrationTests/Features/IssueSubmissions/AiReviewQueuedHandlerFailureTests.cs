using System.Linq.Expressions;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Features.IssueSubmissions.Handlers;
using ProgressService.Domain.IssueSubmissions;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.IssueSubmissions;

public sealed class AiReviewQueuedHandlerFailureTests
{
    [Fact]
    public async Task Queued_event_older_than_completed_iteration_does_not_close_manual_review_gate()
    {
        IssueSubmission submission = IssueSubmission.Create(
            Guid.CreateVersion7(),
            AttemptNumber.Create(1).Value,
            IssueSubmissionPayload.Create("https://github.com/example/repository/pull/1").Value).Value;
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        IIssueSubmissionRepository repository = Substitute.For<IIssueSubmissionRepository>();
        repository.GetByAsync(
                Arg.Any<Expression<Func<IssueSubmission, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(submission);

        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Success<Error>());

        var completedHandler = new AiReviewIterationCompletedHandler(
            repository,
            transactionManager,
            NullLogger<AiReviewIterationCompletedHandler>.Instance);
        var queuedHandler = new AiReviewQueuedForSubmissionHandler(
            repository,
            transactionManager,
            NullLogger<AiReviewQueuedForSubmissionHandler>.Instance);

        await completedHandler.HandleAsync(
            new AiReviewIterationCompleted(
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                submission.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                IterationNumber: 1,
                Verdict: string.Empty,
                GitHubReviewId: null,
                CompletedAt: completedAt),
            CancellationToken.None);

        await queuedHandler.HandleAsync(
            new AiReviewQueuedForSubmission(
                Guid.CreateVersion7(),
                submission.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                QueuedAt: completedAt.AddSeconds(-1)),
            CancellationToken.None);

        Assert.True(submission.ReadyForHumanReview);
        Assert.Equal("FAILED", submission.AiReviewStatus);
        Assert.Equal(1, submission.AiIterationsCount);
        await transactionManager.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_failure_is_rethrown_for_message_retry()
    {
        IssueSubmission submission = IssueSubmission.Create(
            Guid.CreateVersion7(),
            AttemptNumber.Create(1).Value,
            IssueSubmissionPayload.Create("https://github.com/example/repository/pull/1").Value).Value;

        IIssueSubmissionRepository repository = Substitute.For<IIssueSubmissionRepository>();
        repository.GetByAsync(
                Arg.Any<Expression<Func<IssueSubmission, bool>>>(),
                Arg.Any<CancellationToken>())
            .Returns(submission);

        ITransactionManager transactionManager = Substitute.For<ITransactionManager>();
        transactionManager.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));

        var handler = new AiReviewQueuedForSubmissionHandler(
            repository,
            transactionManager,
            NullLogger<AiReviewQueuedForSubmissionHandler>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => handler.HandleAsync(
            new AiReviewQueuedForSubmission(
                Guid.CreateVersion7(),
                submission.Id,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                DateTimeOffset.UtcNow),
            CancellationToken.None));
    }
}