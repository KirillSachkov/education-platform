namespace ProgressService.Domain.IssueSubmissions;

/// <summary>
///     Статус проверки попытки сдачи задачи.
/// </summary>
public enum IssueSubmissionReviewStatus
{
    /// <summary>Ожидает назначения проверяющего.</summary>
    PENDING,

    /// <summary>Проверяющий рассматривает работу.</summary>
    IN_REVIEW,

    /// <summary>Попытка одобрена.</summary>
    APPROVED,

    /// <summary>Требуются доработки.</summary>
    CHANGES_REQUESTED,
}
