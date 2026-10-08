namespace ProgressService.Domain.Modules;

/// <summary>
///     Статус выполнения элемента модуля.
/// </summary>
public enum ModuleItemProgressStatus
{
    /// <summary>Элемент не выполнен.</summary>
    NOT_COMPLETED,

    /// <summary>Элемент выполнен.</summary>
    COMPLETED,
}
