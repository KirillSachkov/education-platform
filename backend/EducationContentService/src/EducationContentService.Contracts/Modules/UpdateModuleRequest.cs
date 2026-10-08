namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Запрос на обновление модуля.
/// </summary>
public sealed record UpdateModuleRequest(string Title, string? Description, string? DetailedDescription = null);
