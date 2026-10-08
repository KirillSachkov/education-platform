namespace ProgressService.Contracts.Dtos;

public sealed record IssueLatestSubmissionDto(
    Guid SubmissionId,
    string Payload,
    string ReviewStatus,
    DateTime SubmittedAt,
    DateTime? ReviewStartedAt,
    DateTime? ReviewedAt,
    string? Feedback);
