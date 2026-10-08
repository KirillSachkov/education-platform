namespace Shared.Messaging.IntegrationEvents.Progress.Events;

/// <summary>
///     Published when a student presses «Задать вопрос автору» on an issue page BEFORE
///     submitting a solution (#693). Unlike <see cref="IssueSubmissionAuthorHelpRequested"/>
///     (#383), which is scoped to an existing submission in the review flow, this is a private,
///     issue-scoped channel a student can use while still reading / stuck on a task — there is
///     no submission yet. Recipient of the resulting notification: the course author
///     (<paramref name="AuthorId"/>).
///
///     Idempotent on the publisher side: <c>IssueAuthorQuestion</c> is a single row per
///     <c>(StudentUserId, IssueId)</c> pair; a repeated ask is a no-op (success) and the
///     endpoint only publishes on the first insert.
/// </summary>
public sealed record IssueAuthorQuestionAsked(
    Guid QuestionId,
    Guid StudentUserId,
    Guid AuthorId,
    Guid IssueId,
    // Primary course binding of the issue, if any. Null for an unbound issue — the handler
    // falls back to a non-course deep-link.
    Guid? CourseId,
    string Message,
    DateTimeOffset AskedAt);
