namespace AssignmentReviewService.Core.Maintenance;

/// <summary>
///     Конфиг для <see cref="DeadLetterCleanupBackgroundService"/>.
///     Section <c>AssignmentReviewMaintenance:DeadLetterCleanup</c>.
/// </summary>
public sealed class DeadLetterCleanupOptions
{
    public const string SECTION_NAME = "AssignmentReviewMaintenance:DeadLetterCleanup";

    /// <summary>Включить cron-cleanup. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Дропать dead-letter envelope'ы старше этого окна. Default 30 дней —
    ///     достаточно времени, чтобы разобрать инциденты, но не дать таблице
    ///     бесконечно расти.
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Периодичность запуска. Default раз в сутки.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);
}
