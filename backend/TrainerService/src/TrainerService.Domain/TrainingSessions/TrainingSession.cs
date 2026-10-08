using SharedKernel.DomainEvents;
using TrainerService.Domain.TrainingSessions.Events;

namespace TrainerService.Domain.TrainingSessions;

/// <summary>
/// Aggregate root: одна тренировочная сессия (DRILL/MOCK/CHALLENGE). Содержит снапшот
/// выбранных вопросов (<see cref="Items"/>), копит ответы, считает итоговый балл.
/// </summary>
public sealed class TrainingSession : AggregateRoot
{
    private readonly List<TrainingSessionItem> _items = [];

    private TrainingSession() { } // EF

    private TrainingSession(
        Guid id,
        Guid userId,
        TrainingMode mode,
        Guid? trackId,
        IReadOnlyList<Guid> topicIds,
        int? timeLimitSeconds,
        RevealPolicy revealPolicy)
    {
        Id = id;
        UserId = userId;
        Mode = mode;
        TrackId = trackId;
        TopicIds = topicIds;
        TimeLimitSeconds = timeLimitSeconds;
        RevealPolicy = revealPolicy;
        Status = SessionStatus.IN_PROGRESS;
        GradingStatus = GradingStatus.NOT_REQUIRED;
        StartedAt = DateTime.UtcNow;
        Version = Guid.CreateVersion7();
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>
    ///     Optimistic-concurrency token for the whole session aggregate. Every mutation, including a
    ///     child-item answer, rotates it so two stale requests cannot silently overwrite each other.
    /// </summary>
    public Guid Version { get; private set; }

    public TrainingMode Mode { get; private set; }

    /// <summary>
    ///     Трек, к которому относится сессия. DRILL — трек темы; MOCK — трек запроса. Хранится
    ///     явно (а не выводится из <see cref="TopicIds"/>) чтобы фильтр истории по треку был
    ///     SQL-индексируемым равенством, без array-overlap, который Npgsql не транслирует (#568).
    ///     <c>null</c> для mock-собеса (mock-interview), который не привязан к одному треку —
    ///     набирает вопросы по своим темам кросс-трекно (#568).
    /// </summary>
    public Guid? TrackId { get; private set; }

    /// <summary>Темы, по которым собрана сессия. Хранится как Postgres <c>uuid[]</c>.</summary>
    public IReadOnlyList<Guid> TopicIds { get; private set; } = [];

    /// <summary>
    ///     Лимит времени на сессию в секундах (MOCK — симуляция собеса с таймером). Null —
    ///     без лимита (DRILL). Хранится информативно — клиент сам обрабатывает обратный отсчёт;
    ///     сервер не отвергает ответы по истечении (Ф1 — без серверного авто-фейла по таймеру).
    /// </summary>
    public int? TimeLimitSeconds { get; private set; }

    /// <summary>
    ///     Когда раскрывается правильный ответ (Ф2, #568). По умолчанию <see cref="RevealPolicy.END_OF_SESSION"/>
    ///     (summative-тест: счёт копится, разбор после Complete); <see cref="RevealPolicy.PER_QUESTION"/> —
    ///     мгновенный фидбэк (formative). Хранится как строка <c>HasConversion&lt;string&gt;()</c>.
    /// </summary>
    public RevealPolicy RevealPolicy { get; private set; }

    public SessionStatus Status { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    public int? ScorePercent { get; private set; }

    /// <summary>
    ///     Статус AI-грейдинга открытых ответов (#585). По умолчанию <see cref="GradingStatus.NOT_REQUIRED"/>;
    ///     мок-собес с OPEN_TEXT-ответами после Complete переводится в PENDING → GRADING → GRADED.
    /// </summary>
    public GradingStatus GradingStatus { get; private set; }

    /// <summary>Итоговый AI-фидбэк по мок-собесу (что хорошо/чего не хватает). Null до GRADED.</summary>
    public string? AiOverallFeedback { get; private set; }

    /// <summary>JSON-массив слабых тем из AI-разбора (<c>string[]</c>). Десериализуется в DTO.</summary>
    public string? AiWeakTopicsJson { get; private set; }

    /// <summary>JSON-массив сильных сторон из AI-разбора (<c>string[]</c>). Десериализуется в DTO.</summary>
    public string? AiStrengthsJson { get; private set; }

    public IReadOnlyList<TrainingSessionItem> Items => _items;

    public static Result<TrainingSession, Error> Create(
        Guid userId,
        TrainingMode mode,
        Guid? trackId,
        IReadOnlyList<Guid> topicIds,
        int? timeLimitSeconds = null,
        RevealPolicy revealPolicy = RevealPolicy.END_OF_SESSION)
    {
        if (topicIds.Count == 0)
            return TrainerServiceErrors.Session.NoTopics();

        TrainingSession session = new(
            Guid.CreateVersion7(), userId, mode, trackId, topicIds, timeLimitSeconds, revealPolicy);
        session.RaiseDomainEvent(new TrainingSessionStartedEvent(session.Id, userId, mode));
        return session;
    }

    public UnitResult<Error> AddItem(
        Guid questionId,
        Guid topicId,
        string questionType,
        string questionText,
        string optionsJson,
        string? section,
        string? difficulty,
        int sortIndex,
        string? gradingKeyJson)
    {
        if (Status != SessionStatus.IN_PROGRESS)
            return TrainerServiceErrors.Session.NotInProgress();

        TrainingSessionItem item = TrainingSessionItem.Create(
            questionId,
            topicId,
            questionType,
            questionText,
            optionsJson,
            section,
            difficulty,
            sortIndex,
            gradingKeyJson);

        _items.Add(item);
        Touch();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> RecordAnswer(
        Guid itemId,
        string? answerRaw,
        int? scorePercent,
        AnswerVerdict verdict,
        string? feedback)
    {
        if (Status != SessionStatus.IN_PROGRESS)
            return TrainerServiceErrors.Session.NotInProgress();

        TrainingSessionItem? item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return TrainerServiceErrors.Session.ItemNotFound(itemId);

        item.RecordAnswer(answerRaw, scorePercent, verdict, feedback);
        Touch();
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Complete(int scorePercent)
    {
        if (Status == SessionStatus.COMPLETED)
            return TrainerServiceErrors.Session.AlreadyCompleted();

        if (Status != SessionStatus.IN_PROGRESS)
            return TrainerServiceErrors.Session.NotInProgress();

        Status = SessionStatus.COMPLETED;
        ScorePercent = Math.Clamp(scorePercent, 0, 100);
        CompletedAt = DateTime.UtcNow;
        Touch();
        RaiseDomainEvent(new TrainingSessionCompletedEvent(Id, UserId, ScorePercent.Value));
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Abandon()
    {
        if (Status != SessionStatus.IN_PROGRESS)
            return TrainerServiceErrors.Session.NotInProgress();

        Status = SessionStatus.ABANDONED;
        CompletedAt = DateTime.UtcNow;
        Touch();
        return UnitResult.Success<Error>();
    }

    // --- AI grading (#585) — runs AFTER Complete, so none of these require IN_PROGRESS. ---

    /// <summary>Помечает сессию как ожидающую AI-грейдинга (выставляется при Complete мок-собеса с открытыми ответами).</summary>
    public void MarkGradingPending()
    {
        GradingStatus = GradingStatus.PENDING;
        Touch();
    }

    /// <summary>Помечает грейдинг как идущий (background-грейдер взял сессию в работу).</summary>
    public void MarkGrading()
    {
        GradingStatus = GradingStatus.GRADING;
        Touch();
    }

    /// <summary>Помечает грейдинг как проваленный (неустранимый сбой AI — фронт покажет авто-баллы без AI-фидбэка).</summary>
    public void MarkGradingFailed()
    {
        GradingStatus = GradingStatus.FAILED;
        Touch();
    }

    /// <summary>Помечает грейдинг как завершённый.</summary>
    public void MarkGraded()
    {
        GradingStatus = GradingStatus.GRADED;
        Touch();
    }

    /// <summary>
    ///     Применяет AI-вердикт к одному item'у (открытый ответ). Находит item по id и делегирует
    ///     ему запись вердикта/балла/фидбэка. Не трогает <c>AnsweredAt</c> (ответ уже дан).
    /// </summary>
    public UnitResult<Error> ApplyAiGrade(Guid itemId, AnswerVerdict verdict, int scorePercent, string? feedback)
    {
        TrainingSessionItem? item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return TrainerServiceErrors.Session.ItemNotFound(itemId);

        item.ApplyAiGrade(verdict, scorePercent, feedback);
        Touch();
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Пересчитывает итоговый балл сессии после AI-грейдинга открытых ответов (#585). Грейдер
    ///     считает среднее по item'ам, у которых теперь ЕСТЬ балл (авто-грейдимые + оценённые открытые),
    ///     и кладёт его сюда. Clamps 0..100.
    /// </summary>
    public void SetFinalScore(int scorePercent)
    {
        ScorePercent = Math.Clamp(scorePercent, 0, 100);
        Touch();
    }

    /// <summary>
    ///     Записывает итоговый AI-разбор мок-собеса: overall-фидбэк + JSON-сериализованные массивы
    ///     слабых тем и сильных сторон. Вызывается грейдером перед <see cref="MarkGraded"/>.
    /// </summary>
    public void RecordAiOverall(string? overallFeedback, string? weakTopicsJson, string? strengthsJson)
    {
        AiOverallFeedback = overallFeedback;
        AiWeakTopicsJson = weakTopicsJson;
        AiStrengthsJson = strengthsJson;
        Touch();
    }

    private void Touch() => Version = Guid.CreateVersion7();
}
