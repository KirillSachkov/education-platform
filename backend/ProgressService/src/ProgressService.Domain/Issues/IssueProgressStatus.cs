namespace ProgressService.Domain.Issues;

/// <summary>
///     Статус прогресса по задаче.
/// </summary>
public enum IssueProgressStatus
{
    /// <summary>Работа не начата.</summary>
    NOT_STARTED,

    /// <summary>Студент работает над задачей.</summary>
    IN_PROGRESS,

    /// <summary>Задача отправлена на проверку.</summary>
    UNDER_REVIEW,

    /// <summary>Проверяющий запросил доработки.</summary>
    REQUESTED_CHANGES,

    /// <summary>Задача успешно выполнена.</summary>
    COMPLETED,
}
