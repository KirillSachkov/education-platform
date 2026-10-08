namespace EducationContentService.Contracts.Issues;

public sealed record UpdateReviewSpecRequest(
    string? AuthorPrompt,
    string? ReviewAspects,
    bool IsAutoReviewEnabled);
