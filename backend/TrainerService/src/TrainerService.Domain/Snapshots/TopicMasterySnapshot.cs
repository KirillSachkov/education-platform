namespace TrainerService.Domain.Snapshots;

/// <summary>
/// Aggregate root: точечный снимок mastery пользователя по теме на дату (#681 T1). Фоновый джоб
/// раз в сутки копирует текущее <c>topic_masteries.mastery_percent</c> в строку за день — так
/// получается история mastery, по которой студенту показывается сравнение «vs месяц назад».
/// Чистая запись учёта: после создания не мутируется; повторный прогон за тот же день заменяет
/// строки дня. Unique на <c>(SnapshotDate, UserId, TopicId)</c>.
/// </summary>
public sealed class TopicMasterySnapshot
{
    private TopicMasterySnapshot() { } // EF

    private TopicMasterySnapshot(
        Guid id,
        DateOnly snapshotDate,
        Guid userId,
        Guid topicId,
        double mastery,
        DateTimeOffset createdAt)
    {
        Id = id;
        SnapshotDate = snapshotDate;
        UserId = userId;
        TopicId = topicId;
        Mastery = mastery;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DateOnly SnapshotDate { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TopicId { get; private set; }

    /// <summary>Mastery (0..100) пользователя по теме на момент снимка.</summary>
    public double Mastery { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TopicMasterySnapshot Create(DateOnly snapshotDate, Guid userId, Guid topicId, double mastery) =>
        new(Guid.CreateVersion7(), snapshotDate, userId, topicId, mastery, DateTimeOffset.UtcNow);
}
