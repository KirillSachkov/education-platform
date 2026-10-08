namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Запрос на изменение приоритета просмотра элемента модуля.
/// </summary>
public sealed record UpdateModuleItemViewPriorityRequest(string ViewPriority);
