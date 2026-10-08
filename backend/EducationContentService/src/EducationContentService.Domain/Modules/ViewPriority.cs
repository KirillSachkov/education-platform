namespace EducationContentService.Domain.Modules;

/// <summary>
///     Приоритет просмотра элемента модуля.
///     Визуальная подсказка для студента, не влияет на расчёт прогресса.
/// </summary>
public enum ViewPriority
{
    Key,
    Recommended,
    Supplementary
}
