namespace ProgressService.Contracts.Requests;

public sealed record GetReviewIssuesRequest(
    int Page,
    int PageSize,
    Guid? CourseId,
    string? Cursor = null,
    Guid? SubmissionId = null);
