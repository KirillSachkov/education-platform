namespace Shared.Messaging.IntegrationEvents.Progress.Events;

/// <summary>
/// Published when a reviewer approves an issue submission.
/// Recipient of resulting notification: the submission author (<paramref name="UserId"/>).
/// </summary>
public sealed record IssueSubmissionApproved(
    Guid SubmissionId,
    Guid UserId,
    Guid IssueId,
    Guid CourseId,
    Guid ReviewerId,
    string? Comment,
    DateTimeOffset ReviewedAt);
