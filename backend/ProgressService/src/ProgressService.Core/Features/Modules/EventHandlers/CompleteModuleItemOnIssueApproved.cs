using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Modules.EventHandlers;

/// <summary>
/// Завершает элемент модуля по задаче и обновляет счетчики модуля при одобрении задачи.
/// Внутрипроцессный эффект: запись в ту же БД в рамках той же транзакции.
/// </summary>
public sealed class CompleteModuleItemOnIssueApproved
    : IDomainEventHandler<IssueProgressApprovedEvent>
{
    private readonly IModuleProgressService _moduleProgressService;

    public CompleteModuleItemOnIssueApproved(IModuleProgressService moduleProgressService)
    {
        _moduleProgressService = moduleProgressService;
    }

    public async Task<UnitResult<Error>> Handle(IssueProgressApprovedEvent domainEvent, CancellationToken cancellationToken)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        var result = await _moduleProgressService.CompleteIssueModuleItemAsync(
            issueProgress.EnrollmentId,
            issueProgress.IssueId,
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error;
        }

        return UnitResult.Success<Error>();
    }
}
