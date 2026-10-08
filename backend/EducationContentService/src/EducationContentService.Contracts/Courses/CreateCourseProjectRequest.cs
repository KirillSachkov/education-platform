namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Запрос на создание проекта в курсе.
/// </summary>
public sealed record CreateCourseProjectRequest(
    string Title,
    string? Description = null,
    string? DetailedDescription = null,
    bool RequiresGithubConnection = true,
    bool RequiresReviewApp = true,
    bool IsAutoReviewEnabled = true);
