namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Issue #546: авто-ран AI-проверки пропущен — reviewable diff PR'а выше hard-cap'ов
///     (<c>HardMaxDiffAdditions</c> / <c>HardMaxFiles</c>). Публикуется ОДИН раз на AiReview
///     (повторные too_large-фейлы того же review дедупятся на стороне ARS); ручной запуск
///     с <c>AllowOversizedDiff</c> событие не публикует — cap уже снят.
///     Consumer — NotificationService: уведомление автору курса с CTA запустить проверку
///     вручную («Перепроверить» ревьюит PR целиком, разбивая на части).
/// </summary>
public sealed record AiReviewOversizedSkipped(
    Guid AiReviewId,
    Guid SubmissionId,
    Guid StudentUserId,
    Guid AuthorId,
    Guid IssueId,
    string RepoFullName,
    int PullNumber,
    DateTimeOffset OccurredAt);
