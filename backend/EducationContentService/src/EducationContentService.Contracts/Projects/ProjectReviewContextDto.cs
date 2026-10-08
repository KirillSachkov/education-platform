namespace EducationContentService.Contracts.Projects;

public sealed record ProjectReviewContextDto(
    Guid Id,
    Guid ProjectId,
    string GuidelinesMarkdown,
    bool IsAutoReviewEnabled,
    bool RequiresGithubConnection,
    bool RequiresReviewApp,
    DateTime UpdatedAt);
