namespace ProgressService.Domain.Projects;

/// <summary>
///     Статус прогресса по проекту.
/// </summary>
public enum ProjectProgressStatus
{
    /// <summary>Работа не начата.</summary>
    NOT_STARTED,

    /// <summary>Студент решает задачи проекта.</summary>
    IN_PROGRESS,

    /// <summary>Все задачи проекта завершены.</summary>
    COMPLETED,
}
