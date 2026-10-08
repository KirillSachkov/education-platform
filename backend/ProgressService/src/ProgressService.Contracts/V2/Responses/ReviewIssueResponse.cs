namespace ProgressService.Contracts.V2.Responses;

/// <summary>
///     V2: Результат проверки задачи.
/// </summary>
public sealed record ReviewIssueResponse(
    Guid SubmissionId,
    Guid IssueId,
    string ReviewStatus,
    Guid ReviewerId,
    string? Feedback,
    DateTime ReviewedAt);
