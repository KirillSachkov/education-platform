using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Issues.EventHandlers;

/// <summary>
/// Синхронизирует статус IssueProgress при одобрении submission.
/// Внутрипроцессный эффект: запись в ту же БД в рамках той же транзакции.
/// </summary>
public sealed class ApproveIssueProgressOnSubmissionApproved
    : IDomainEventHandler<IssueSubmissionApproveEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;

    public ApproveIssueProgressOnSubmissionApproved(IIssueProgressRepository issueProgressRepository)
    {
        _issueProgressRepository = issueProgressRepository;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionApproveEvent domainEvent, CancellationToken ct)
    {
        IssueSubmission submission = domainEvent.Submission;

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(x => x.Id == submission.IssueProgressId, ct);
        if (issueProgressResult.IsFailure)
        {
            return issueProgressResult.Error;
        }

        return issueProgressResult.Value.Approve();
    }
}
