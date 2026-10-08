namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Опубликован AssignmentReviewService при завершении одной AI-iteration
///     (success или failure). Consumer на Phase 8 — ProgressService —
///     обновляет denorm-поля на issue_submission (LatestAiVerdict / IterationsCount /
///     LastAiIterationAt) для author review-inbox query.
///
///     <see cref="Verdict"/> пустой при failure'е (см. <see cref="GitHubReviewId"/>
///     == null indicates iteration не зафиксировала review в PR).
/// </summary>
public sealed record AiReviewIterationCompleted(
    Guid AiReviewId,
    Guid IterationId,
    Guid SubmissionId,
    Guid UserId,
    Guid IssueId,
    int IterationNumber,
    string Verdict,
    long? GitHubReviewId,
    DateTimeOffset CompletedAt);
