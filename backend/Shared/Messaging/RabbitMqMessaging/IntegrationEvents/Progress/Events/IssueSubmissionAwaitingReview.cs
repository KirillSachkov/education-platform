namespace Shared.Messaging.IntegrationEvents.Progress.Events;

/// <summary>
///     Published when a student submits an issue solution and it's now awaiting review
///     (human author + AI). Recipients:
///     <list type="bullet">
///         <item>NotificationService — выдаёт автору уведомление о ревью.</item>
///         <item>AssignmentReviewService (Phase 8 #15) — создаёт <c>AiReview</c>
///         если есть active VCS installation на owner'а repo'а из <see cref="Payload"/>.</item>
///     </list>
///     <see cref="Payload"/> — submission URL (как правило GitHub PR <c>https://github.com/owner/repo/pull/N</c>),
///     добавлено в Phase 8 для ARS-binding'а. Существующие consumers (Notifications)
///     не используют поле и игнорируют его.
///     <see cref="AiReviewRequested"/> отделяет author notification от AI auto-flow:
///     ProgressService публикует то же событие для ручного ревью, но ARS пропускает
///     submission, если AI отключена на уровне проекта/задачи.
/// </summary>
public sealed record IssueSubmissionAwaitingReview(
    Guid SubmissionId,
    Guid StudentUserId,
    Guid AuthorId,
    Guid IssueId,
    Guid CourseId,
    DateTimeOffset SubmittedAt,
    string Payload,
    bool AiReviewRequested = true);
