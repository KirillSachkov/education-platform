using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

/// <summary>
/// Поднимается, когда ученик отправил решение задачи на проверку.
/// Обрабатывается handler'ом, который публикует integration event
/// <c>IssueSubmissionAwaitingReview</c> для автора курса (уведомление о ревью).
/// </summary>
public sealed record IssueSubmissionCreatedEvent(IssueSubmission Submission) : IDomainEvent;
