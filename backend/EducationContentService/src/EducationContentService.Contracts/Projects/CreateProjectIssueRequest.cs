namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Запрос на создание задачи в проекте.
/// </summary>
public sealed record CreateProjectIssueRequest(
    string Title,
    string Content,
    string SubmissionMode = "PULL_REQUEST",
    string? SelfCheckInstructions = null);
