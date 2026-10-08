namespace TrainerService.Domain.TrainingSessions;

/// <summary>
/// Child entity в <see cref="TrainingSession.Items"/>: снапшот выбранного вопроса
/// + ответ пользователя + результат грейдинга. Снапшот (Text/OptionsJson/...) делает
/// сессию устойчивой к последующим правкам банка (паттерн level-test / #556/#561).
/// PK выставляется EF через TimeOrderedGuidValueGenerator (Id=Guid.Empty в factory —
/// nav-collection child rule, см. docs/agents/backend-transactions.md).
/// </summary>
public sealed class TrainingSessionItem
{
    private TrainingSessionItem() { } // EF

    private TrainingSessionItem(
        Guid id,
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
        Id = id;
        QuestionId = questionId;
        TopicId = topicId;
        QuestionType = questionType;
        QuestionText = questionText;
        OptionsJson = optionsJson;
        Section = section;
        Difficulty = difficulty;
        SortIndex = sortIndex;
        GradingKeyJson = gradingKeyJson;
        Verdict = AnswerVerdict.PENDING;
    }

    public Guid Id { get; private set; }

    public Guid QuestionId { get; private set; }

    /// <summary>
    ///     Тема-источник этого вопроса. Несётся на item'е (а не только в <c>TrainingSession.TopicIds</c>),
    ///     чтобы (а) CheckAnswer обновлял mastery именно ПРАВИЛЬНОЙ темы в multi-topic MOCK-сессии и
    ///     (б) разбивка результатов MOCK считалась per-topic. У DRILL = единственная тема сессии.
    /// </summary>
    public Guid TopicId { get; private set; }

    public string QuestionType { get; private set; } = null!;

    public string QuestionText { get; private set; } = null!;

    /// <summary>JSONB-снапшот вариантов ответа: <c>[{id,text}]</c>.</summary>
    public string OptionsJson { get; private set; } = null!;

    public string? Section { get; private set; }

    public string? Difficulty { get; private set; }

    public int SortIndex { get; private set; }

    /// <summary>
    ///     JSONB-снапшот ключа грейдинга (<c>{correctOptionIds,referenceAnswer,explanation}</c>),
    ///     зафиксированный при выдаче вопроса. <b>Server-only</b> — НИКОГДА не отдаётся наружу
    ///     в DTO; используется только грейдером для проверки ответа. Снапшот делает грейдинг
    ///     устойчивым к последующим правкам банка (паттерн level-test / #556/#561).
    /// </summary>
    public string? GradingKeyJson { get; private set; }

    public string? AnswerRaw { get; private set; }

    public int? ScorePercent { get; private set; }

    public AnswerVerdict? Verdict { get; private set; }

    public string? Feedback { get; private set; }

    public DateTime? AnsweredAt { get; private set; }

    internal static TrainingSessionItem Create(
        Guid questionId,
        Guid topicId,
        string questionType,
        string questionText,
        string optionsJson,
        string? section,
        string? difficulty,
        int sortIndex,
        string? gradingKeyJson) =>
        new(
            Guid.Empty,
            questionId,
            topicId,
            questionType,
            questionText,
            optionsJson,
            section,
            difficulty,
            sortIndex,
            gradingKeyJson);

    internal void RecordAnswer(string? answerRaw, int? scorePercent, AnswerVerdict verdict, string? feedback)
    {
        AnswerRaw = answerRaw;
        ScorePercent = scorePercent;
        Verdict = verdict;
        Feedback = feedback;
        AnsweredAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Применяет результат AI-грейдинга открытого ответа (#585): выставляет вердикт/балл/AI-фидбэк.
    ///     <c>AnsweredAt</c> НЕ трогается — ответ уже был дан студентом, грейдинг идёт пост-фактум.
    /// </summary>
    internal void ApplyAiGrade(AnswerVerdict verdict, int scorePercent, string? feedback)
    {
        Verdict = verdict;
        ScorePercent = scorePercent;
        Feedback = feedback;
    }
}
