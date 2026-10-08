namespace Shared.Messaging.IntegrationEvents.AssignmentReview;

/// <summary>
///     Published AssignmentReviewService'ом, когда <c>AiReview</c> создан под
///     submission и есть active installation (= можно запустить iteration).
///     Consumer — ProgressService, гейтит <c>ReadyForHumanReview=false</c> на
///     <c>IssueSubmission</c>: автор не должен видеть submission в inbox'е, пока
///     студент не прогнал AI iteration и не вызвал finalize.
///
///     Если installation отсутствует, AiReview создаётся с <c>Status=FAILED</c>
///     и этот event НЕ публикуется (submission остаётся ReadyForHumanReview=true,
///     уходит в author inbox по нормальному пути).
/// </summary>
public sealed record AiReviewQueuedForSubmission(
    Guid AiReviewId,
    Guid SubmissionId,
    Guid UserId,
    Guid IssueId,
    DateTimeOffset QueuedAt);
