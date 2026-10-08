namespace EducationContentService.Contracts.Projects;

/// <summary>
///     Запрос на обновление проекта.
/// </summary>
public sealed record UpdateProjectRequest(string Title, string? Description = null, string? DetailedDescription = null);
