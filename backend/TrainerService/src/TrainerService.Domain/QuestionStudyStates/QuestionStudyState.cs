using SharedKernel.DomainEvents;

namespace TrainerService.Domain.QuestionStudyStates;

/// <summary>
/// Aggregate root: study-state пользователя по конкретному вопросу ECS-квиза (#568 Ф2). Питает
/// список вопросов («новый / видел / знаю / на повтор / ошибся»), «Мои ошибки», прогресс и
/// кросс-тематическую SRS-очередь «на повтор сегодня». Unique на (UserId, QuestionId) — одна
/// строка на пару. Отсутствие строки = «новый» вопрос (NEW не хранится отдельным статусом).
///
/// <para><see cref="QuestionId"/> — id вопроса ECS-квиза (cross-service, без FK). <see cref="TopicId"/> —
/// тема тренажёра, в рамках которой вопрос изучался (для per-topic срезов «Моих ошибок»).</para>
///
/// SRS — Anki SM-2-lite (<see cref="Sm2Scheduler"/>). Результат теста (<see cref="RecordTestResult"/>)
/// двигает интервал.
/// </summary>
public sealed class QuestionStudyState : AggregateRoot
{
    private QuestionStudyState() { } // EF

    private QuestionStudyState(Guid id, Guid userId, Guid questionId, Guid topicId)
    {
        Id = id;
        UserId = userId;
        QuestionId = questionId;
        TopicId = topicId;
        Status = StudyStatus.SEEN;
        LastSeenAt = DateTimeOffset.UtcNow;
        NextDueAt = null;
        TimesSeen = 0;
        TimesKnown = 0;
        TimesWrong = 0;
        EaseFactor = Sm2Scheduler.DefaultEaseFactor;
        IntervalDays = 0;
        Repetitions = 0;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Id вопроса ECS-квиза, который трекает это состояние (без FK — cross-service граница).</summary>
    public Guid QuestionId { get; private set; }

    /// <summary>Тема тренажёра, в рамках которой вопрос изучался.</summary>
    public Guid TopicId { get; private set; }

    public StudyStatus Status { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Когда вопрос снова «на повтор» по SRS. Null — ещё не запланирован (только увиден).</summary>
    public DateTimeOffset? NextDueAt { get; private set; }

    public int TimesSeen { get; private set; }

    public int TimesKnown { get; private set; }

    public int TimesWrong { get; private set; }

    /// <summary>SM-2 ease-factor (≥ 1.3). Старт 2.5.</summary>
    public double EaseFactor { get; private set; }

    /// <summary>SM-2 текущий интервал в днях.</summary>
    public int IntervalDays { get; private set; }

    /// <summary>SM-2 счётчик успешных повторений подряд (сбрасывается в 0 на ошибке).</summary>
    public int Repetitions { get; private set; }

    public static QuestionStudyState Create(Guid userId, Guid questionId, Guid topicId) =>
        new(Guid.CreateVersion7(), userId, questionId, topicId);

    /// <summary>
    /// Результат авто-грейдимого ответа в тест/learn-сессии: верно (<paramref name="correct"/>=true) или
    /// неверно. Двигает счётчики, статус и SRS-расписание.
    /// </summary>
    public void RecordTestResult(bool correct, DateTimeOffset now) => Apply(correct, now);

    /// <summary>
    /// Мягкая самооценка «Не уверен» (#691 t8): паркует вопрос на повтор. В отличие от
    /// <see cref="RecordTestResult"/>(false), НЕ помечает ответ неверным (статус → <see cref="StudyStatus.REVIEW"/>,
    /// а не WRONG), НЕ бампает <see cref="TimesWrong"/> и НЕ трогает mastery (это не оценённый ответ).
    /// Планирует ближний повтор тем же SM-2-планировщиком (как lapse → завтра), штампует
    /// <see cref="LastSeenAt"/> и бампает только <see cref="TimesSeen"/>. Вопрос всплывёт в «На повтор»
    /// (SRS due) и «Моих ошибках» как REVIEW. Идемпотентно по статусу — повторный вызов держит REVIEW.
    /// </summary>
    public void MarkForReview(DateTimeOffset now)
    {
        TimesSeen++;

        Sm2Scheduler.Sm2State previous = new(EaseFactor, IntervalDays, Repetitions);
        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(previous, success: false, now, out DateTimeOffset nextDueAt);

        EaseFactor = next.EaseFactor;
        IntervalDays = next.IntervalDays;
        Repetitions = next.Repetitions;
        NextDueAt = nextDueAt;
        LastSeenAt = now;
        Status = StudyStatus.REVIEW;
    }

    private void Apply(bool success, DateTimeOffset now)
    {
        TimesSeen++;
        if (success)
            TimesKnown++;
        else
            TimesWrong++;

        Sm2Scheduler.Sm2State previous = new(EaseFactor, IntervalDays, Repetitions);
        Sm2Scheduler.Sm2State next = Sm2Scheduler.Schedule(previous, success, now, out DateTimeOffset nextDueAt);

        EaseFactor = next.EaseFactor;
        IntervalDays = next.IntervalDays;
        Repetitions = next.Repetitions;
        NextDueAt = nextDueAt;
        LastSeenAt = now;
        Status = success ? StudyStatus.KNOWN : StudyStatus.WRONG;
    }
}
