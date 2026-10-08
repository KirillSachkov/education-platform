using SharedKernel.DomainEvents;

namespace TrainerService.Domain.TopicMasteries;

/// <summary>
/// Aggregate root: mastery пользователя по теме. <b>Derived (#691)</b> — пересчитывается из истории
/// ответов как difficulty-weighted average ПОСЛЕДНЕГО балла по каждому УНИКАЛЬНОМУ вопросу
/// (<see cref="MasteryCalculator"/>), а не как EWMA по потоку ответов. Это убивает фарм (повторный
/// ответ на тот же вопрос только обновляет его последний балл) и взвешивает сложность. Источник
/// сигнала «что хромает» — отдельный от прогресса курса. Unique на (UserId, TopicId).
/// </summary>
public sealed class TopicMastery : AggregateRoot
{
    public const int WEAK_THRESHOLD = 60;

    /// <summary>Тема считается сильной при mastery &gt;= 75 (workstream H, #614).</summary>
    public const int STRONG_THRESHOLD = 75;

    /// <summary>
    /// Минимум оценённых ответов, чтобы тема вообще попала в «сильные/слабые» (#614 H):
    /// одна-две попытки — слишком шумный сигнал, не показываем.
    /// </summary>
    public const int MIN_ATTEMPTS_FOR_SIGNAL = 3;

    private TopicMastery() { } // EF

    private TopicMastery(Guid id, Guid userId, Guid topicId)
    {
        Id = id;
        UserId = userId;
        TopicId = topicId;
        MasteryPercent = 0;
        AnswersCount = 0;
        LastPractisedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid TopicId { get; private set; }

    public int MasteryPercent { get; private set; }

    public int AnswersCount { get; private set; }

    public DateTime LastPractisedAt { get; private set; }

    /// <summary>Computed (не хранится): тема считается слабой при mastery &lt; 60.</summary>
    public bool IsWeak => MasteryPercent < WEAK_THRESHOLD;

    public static TopicMastery Create(Guid userId, Guid topicId) =>
        new(Guid.CreateVersion7(), userId, topicId);

    /// <summary>
    /// Записывает производное (derived) mastery, пересчитанное из истории ответов
    /// (<see cref="MasteryCalculator"/>): <paramref name="masteryPercent"/> = difficulty-weighted
    /// average последнего балла по уникальным вопросам, <paramref name="answersCount"/> = число
    /// уникальных отвеченных вопросов. <paramref name="practisedAt"/> — отметка активности (live —
    /// «сейчас», backfill — последний <c>AnsweredAt</c> из истории).
    /// </summary>
    public UnitResult<Error> SetDerived(int masteryPercent, int answersCount, DateTime practisedAt)
    {
        if (masteryPercent is < 0 or > 100)
            return TrainerServiceErrors.Mastery.InvalidScore();

        MasteryPercent = masteryPercent;
        AnswersCount = answersCount;
        LastPractisedAt = practisedAt;
        return UnitResult.Success<Error>();
    }
}
