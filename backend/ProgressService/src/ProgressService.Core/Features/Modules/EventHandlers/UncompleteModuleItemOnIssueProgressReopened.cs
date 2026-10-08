using ProgressService.Core.Abstractions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Issues.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Core.Features.Modules.EventHandlers;

/// <summary>
/// Снимает отметку завершённости ModuleItemProgress по задаче при reopen ревью.
/// Декрементирует <c>ModuleProgress.ItemsCompleted</c> и откатывает статус модуля
/// при необходимости.
/// </summary>
public sealed class UncompleteModuleItemOnIssueProgressReopened
    : IDomainEventHandler<IssueProgressReopenedEvent>
{
    private readonly IModuleProgressService _moduleProgressService;

    public UncompleteModuleItemOnIssueProgressReopened(IModuleProgressService moduleProgressService)
    {
        _moduleProgressService = moduleProgressService;
    }

    public async Task<UnitResult<Error>> Handle(IssueProgressReopenedEvent domainEvent, CancellationToken cancellationToken)
    {
        IssueProgress issueProgress = domainEvent.Progress;

        return await _moduleProgressService.UncompleteIssueModuleItemAsync(
            issueProgress.EnrollmentId,
            issueProgress.IssueId,
            cancellationToken);
    }
}
