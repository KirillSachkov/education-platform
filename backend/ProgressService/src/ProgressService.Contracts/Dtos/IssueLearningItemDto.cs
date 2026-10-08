namespace ProgressService.Contracts.Dtos;

public sealed record IssueLearningItemDto(
    Guid IssueId,
    Guid ProjectId,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    IssueLatestSubmissionDto? LatestSubmission);
