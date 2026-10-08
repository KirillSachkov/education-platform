namespace ProgressService.Domain.Modules;

/// <summary>
///     Тип элемента модуля, по которому отслеживается прогресс.
/// </summary>
public enum ModuleItemProgressType
{
    /// <summary>Практическая задача.</summary>
    ISSUE,

    /// <summary>Тест.</summary>
    QUIZ,

    /// <summary>Контрольная работа.</summary>
    TEST,

    /// <summary>Материал (унифицированный тип урока/статьи).</summary>
    MATERIAL,
}
