using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

/// <summary>
/// Поднимается, когда ревьюер возвращает уже проверенную submission обратно в статус IN_REVIEW.
/// <paramref name="WasApproved"/> = true, если предыдущий статус был APPROVED (нужно откатить XP,
/// project и module progress). Если false — был CHANGES_REQUESTED, дополнительный откат не нужен.
/// </summary>
public sealed record IssueSubmissionReviewReopenedEvent(IssueSubmission Submission, bool WasApproved) : IDomainEvent;
