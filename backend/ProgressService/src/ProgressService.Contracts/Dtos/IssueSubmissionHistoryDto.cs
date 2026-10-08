namespace ProgressService.Contracts.Dtos;

public sealed record IssueSubmissionHistoryDto(
    Guid IssueId,
    string CurrentStatus,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    IReadOnlyList<IssueSubmissionHistoryItemDto> Attempts);
