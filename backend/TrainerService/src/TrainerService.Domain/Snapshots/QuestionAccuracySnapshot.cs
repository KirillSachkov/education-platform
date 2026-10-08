namespace TrainerService.Domain.Snapshots;

/// <summary>
/// Aggregate root: снимок точности по конкретному вопросу за день (#681 T1). Фоновый джоб раз в
/// сутки агрегирует оценённые ответы дня (<c>training_session_items</c>) по <c>question_id</c> —
/// питает аналитику «качество вопроса во времени». Окно — день: <see cref="Attempts"/>/<see cref="Correct"/>
/// считаются по item'ам, отвеченным и оценённым в этот день (T6 при необходимости суммирует диапазон
/// дат для накопительной точности). Чистая запись учёта; повторный прогон за тот же день заменяет
/// строки дня. Unique на <c>(SnapshotDate, QuestionId)</c>.
/// </summary>
public sealed class QuestionAccuracySnapshot
{
    private QuestionAccuracySnapshot() { } // EF

    private QuestionAccuracySnapshot(
        Guid id,
        DateOnly snapshotDate,
        Guid questionId,
        int attempts,
        int correct,
        double accuracyPct,
        DateTimeOffset createdAt)
    {
        Id = id;
        SnapshotDate = snapshotDate;
        QuestionId = questionId;
        Attempts = attempts;
        Correct = correct;
        AccuracyPct = accuracyPct;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public DateOnly SnapshotDate { get; private set; }

    public Guid QuestionId { get; private set; }

    /// <summary>Оценённых попыток по вопросу в этот день (вердикт финальный, не PENDING).</summary>
    public int Attempts { get; private set; }

    /// <summary>Из них с вердиктом CORRECT.</summary>
    public int Correct { get; private set; }

    /// <summary>Точность (0..100) = Correct / Attempts × 100 за день.</summary>
    public double AccuracyPct { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static QuestionAccuracySnapshot Create(
        DateOnly snapshotDate,
        Guid questionId,
        int attempts,
        int correct,
        double accuracyPct) =>
        new(Guid.CreateVersion7(), snapshotDate, questionId, attempts, correct, accuracyPct, DateTimeOffset.UtcNow);
}
