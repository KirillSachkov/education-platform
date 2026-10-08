namespace ProgressService.Contracts.V2.Responses;

/// <summary>
///     V2: Элемент очереди на проверку.
/// </summary>
public sealed record ReviewQueueIssueResponse(
    Guid SubmissionId,
    Guid IssueId,
    int SubmissionNo,
    Guid UserId,
    string ReviewStatus,
    DateTime SubmittedAt);
