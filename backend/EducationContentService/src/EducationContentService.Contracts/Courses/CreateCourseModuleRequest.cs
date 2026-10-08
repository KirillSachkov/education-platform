namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Запрос на создание модуля в курсе.
/// </summary>
public sealed record CreateCourseModuleRequest(string Title, string? Description = null);
