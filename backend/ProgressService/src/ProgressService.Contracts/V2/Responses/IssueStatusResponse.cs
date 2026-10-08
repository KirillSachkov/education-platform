namespace ProgressService.Contracts.V2.Responses;

/// <summary>
///     V2: Статус задачи с информацией о последней попытке.
/// </summary>
public sealed record IssueStatusResponse(
    Guid IssueId,
    string ItemStatus,
    int? LatestSubmissionNo,
    string? LatestReviewStatus,
    string? Feedback);
