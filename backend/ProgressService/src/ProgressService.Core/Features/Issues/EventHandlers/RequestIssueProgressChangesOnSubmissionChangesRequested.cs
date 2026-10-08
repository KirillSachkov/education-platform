using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Issues.EventHandlers;

/// <summary>
/// Синхронизирует статус IssueProgress, когда ревьюер запрашивает изменения по submission.
/// Внутрипроцессный эффект: запись в ту же БД в рамках той же транзакции.
/// </summary>
public sealed class RequestIssueProgressChangesOnSubmissionChangesRequested
    : IDomainEventHandler<IssueSubmissionChangesRequestedEvent>
{
    private readonly IIssueProgressRepository _issueProgressRepository;

    public RequestIssueProgressChangesOnSubmissionChangesRequested(
        IIssueProgressRepository issueProgressRepository)
    {
        _issueProgressRepository = issueProgressRepository;
    }

    public async Task<UnitResult<Error>> Handle(IssueSubmissionChangesRequestedEvent domainEvent, CancellationToken ct)
    {
        IssueSubmission submission = domainEvent.Submission;

        Result<IssueProgress, Error> issueProgressResult = await _issueProgressRepository
            .GetByAsync(x => x.Id == submission.IssueProgressId, ct);
        if (issueProgressResult.IsFailure)
        {
            return issueProgressResult.Error;
        }

        var result = issueProgressResult.Value.RequestChanges();
        
        if (result.IsFailure)
        {
            return result.Error;
        }

        return UnitResult.Success<Error>();
    }
}
