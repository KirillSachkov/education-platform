namespace Shared.Messaging.IntegrationEvents.Progress.Events;

/// <summary>
///     Published when a student presses «Позвать автора» on their submission (#383).
///     The AI is an assistant, not a gatekeeper — the course author is out of the loop by
///     default and only engages when the student summons them. Recipient of the resulting
///     notification: the course author (<paramref name="AuthorId"/>).
///
///     Idempotent on the publisher side: <c>IssueSubmission.RequestAuthorHelp</c> sets a
///     timestamp once; a repeated request is a no-op (success) but still re-publishes is
///     avoided — the endpoint only publishes on the first transition.
/// </summary>
public sealed record IssueSubmissionAuthorHelpRequested(
    Guid SubmissionId,
    Guid IssueProgressId,
    Guid StudentUserId,
    Guid AuthorId,
    Guid IssueId,
    Guid CourseId,
    DateTimeOffset RequestedAt,
    // #575 — опциональный текст «в чём нужна помощь» от студента. Пусто, если не указан.
    // Optional positional param — старые сообщения до #575 десериализуются как пусто.
    string? Message = null);
