using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Issues.EventHandlers;

/// <summary>
/// При reopen submission'а синхронизирует статус IssueProgress: переводит COMPLETED|REQUESTED_CHANGES
/// → UNDER_REVIEW. Если был COMPLETED — IssueProgress сам поднимет
/// <see cref="IssueProgressReopenedEvent"/> для отката XP / project / module прогресса.
/// </summary>
public sealed class ReopenIssueProgressOnSubmissionReviewReopened
    : IDomainEventHandler<IssueSubmissionReviewReopenedEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;

    public ReopenIssueProgressOnSubmissionReviewReopened(IIssueProgressRepository issueProgressRepository)
    {
        _issueProgressRepository = issueProgressRepository;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionReviewReopenedEvent domainEvent, CancellationToken ct)
    {
        IssueSubmission submission = domainEvent.Submission;

        Result<IssueProgress, Error> progressResult = await _issueProgressRepository
            .GetByAsync(x => x.Id == submission.IssueProgressId, ct);
        if (progressResult.IsFailure)
        {
            return progressResult.Error;
        }

        return progressResult.Value.ReopenReview();
    }
}
