using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

/// <summary>
/// Поднимается, когда IssueProgress возвращён из статуса COMPLETED обратно в UNDER_REVIEW.
/// Триггерит обратный каскад: decrement ProjectProgress, uncomplete
/// соответствующий ModuleItemProgress.
/// </summary>
public sealed record IssueProgressReopenedEvent(IssueProgress Progress) : IDomainEvent;