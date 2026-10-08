namespace TrainerService.Domain.Snapshots;

/// <summary>
/// Aggregate root: один платформенный дневной KPI-снимок тренажёра (#681 T1). Идемпотентная
/// материализованная агрегация за <see cref="SnapshotDate"/> (UTC-дата), пересчитываемая фоновым
/// джобом раз в сутки — питает owner-кривую динамики (тренд по дням). Чистая запись учёта (как
/// <c>AiUsageRecord</c>): после создания не мутируется; повторный прогон джоба за тот же день
/// заменяет строку. Стоимость в <b>микрорублях</b> (₽ × 1 000 000) — зеркалит лоджер <c>ai_usage</c>.
/// </summary>
public sealed class DailyStatSnapshot
{
    private DailyStatSnapshot() { } // EF

    private DailyStatSnapshot(
        Guid id,
        DateOnly snapshotDate,
        int sessionsStarted,
        int activeUsers,
        int completedSessions,
        int openGrades,
        long totalCostMicroRub,
        double avgAccuracyPct,
        DateTimeOffset createdAt)
    {
        Id = id;
        SnapshotDate = snapshotDate;
        SessionsStarted = sessionsStarted;
        ActiveUsers = activeUsers;
        CompletedSessions = completedSessions;
        OpenGrades = openGrades;
        TotalCostMicroRub = totalCostMicroRub;
        AvgAccuracyPct = avgAccuracyPct;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    /// <summary>UTC-дата, за которую посчитан снимок. Уникальна (одна строка на день).</summary>
    public DateOnly SnapshotDate { get; private set; }

    /// <summary>Сессий стартовано в этот день (<c>training_sessions.started_at</c>).</summary>
    public int SessionsStarted { get; private set; }

    /// <summary>Distinct пользователей, стартовавших хоть одну сессию в этот день.</summary>
    public int ActiveUsers { get; private set; }

    /// <summary>Сессий завершено в этот день (<c>completed_at</c> в окне дня).</summary>
    public int CompletedSessions { get; private set; }

    /// <summary>Открытых ответов оценено (AI) в этот день (OPEN_TEXT item'ы с проставленным баллом).</summary>
    public int OpenGrades { get; private set; }

    /// <summary>Суммарная стоимость AI-вызовов за день в микрорублях (₽ × 1 000 000), из <c>ai_usage</c>.</summary>
    public long TotalCostMicroRub { get; private set; }

    /// <summary>Средняя точность (балл 0..100) по всем оценённым ответам дня; 0 если оценённых нет.</summary>
    public double AvgAccuracyPct { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static DailyStatSnapshot Create(
        DateOnly snapshotDate,
        int sessionsStarted,
        int activeUsers,
        int completedSessions,
        int openGrades,
        long totalCostMicroRub,
        double avgAccuracyPct) =>
        new(
            Guid.CreateVersion7(),
            snapshotDate,
            sessionsStarted,
            activeUsers,
            completedSessions,
            openGrades,
            totalCostMicroRub,
            avgAccuracyPct,
            DateTimeOffset.UtcNow);
}
