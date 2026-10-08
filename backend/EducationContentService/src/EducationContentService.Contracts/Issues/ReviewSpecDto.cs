namespace EducationContentService.Contracts.Issues;

public sealed record ReviewSpecDto(
    Guid Id,
    Guid IssueId,
    string? AuthorPrompt,
    string? ReviewAspects,
    bool IsAutoReviewEnabled,
    DateTime UpdatedAt);
