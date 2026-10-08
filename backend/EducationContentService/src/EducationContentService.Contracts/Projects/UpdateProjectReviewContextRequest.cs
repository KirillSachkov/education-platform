namespace EducationContentService.Contracts.Projects;

public sealed record UpdateProjectReviewContextRequest(
    string GuidelinesMarkdown,
    bool IsAutoReviewEnabled,
    bool RequiresGithubConnection = true,
    bool RequiresReviewApp = true);
