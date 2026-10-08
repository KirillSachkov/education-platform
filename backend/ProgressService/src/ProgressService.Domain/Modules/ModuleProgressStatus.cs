namespace ProgressService.Domain.Modules;

/// <summary>
///     Статус прогресса по модулю.
/// </summary>
public enum ModuleProgressStatus
{
    /// <summary>Работа не начата.</summary>
    NOT_STARTED, // TODO: ставить сразу в IN_PROGRESS

    /// <summary>Студент проходит модуль.</summary>
    IN_PROGRESS,

    /// <summary>Модуль завершён.</summary>
    COMPLETED,
}
