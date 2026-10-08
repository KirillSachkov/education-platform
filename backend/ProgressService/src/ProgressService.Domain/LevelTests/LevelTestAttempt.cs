namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Попытка прохождения level-test'а (ST-4, #479). Отдельный от <c>QuizAttempt</c>
///     агрегат: level-test — публичная лид-воронка с анонимными попытками
///     (<see cref="AnonymousId"/> из cookie <c>plu_anon_id</c>) и lead-gate'ом результата —
///     полный разбор открывается после клейма попытки залогиненным пользователем
///     (<see cref="Claim"/>). Детерминированный грейдинг (choice + очки по сложности +
///     секции/уровень/рекомендация) выполняется в фабрике; открытые ответы грейдит AI
///     (ST-5) через <see cref="ApplyAiGrades"/> — до этого проценты считаются только по
///     choice-вопросам (честный тизер). Грейдинг-конфиг снапшотится в попытку
///     (<see cref="GradingConfig"/> + Weight/RecommendedCourseId в секциях), поэтому
///     пересчёт не ходит за answer-key и правка квиза задним числом не меняет результат.
/// </summary>
public sealed class LevelTestAttempt
{
    private LevelTestAttempt(
        Guid quizId,
        Guid? userId,
        Guid? anonymousId,
        IReadOnlyList<LevelTestAnswer> answers,
        IReadOnlyList<LevelTestQuestionResult> questionResults,
        LevelTestGradingConfig gradingConfig,
        LevelTestTotals totals,
        LevelTestAiGradingStatus aiGradingStatus)
    {
        Id = Guid.CreateVersion7();
        QuizId = quizId;
        UserId = userId;
        AnonymousId = anonymousId;
        CreatedAt = DateTime.UtcNow;
        Answers = answers;
        QuestionResults = questionResults;
        GradingConfig = gradingConfig;
        SectionScores = totals.Sections;
        OverallPercent = totals.OverallPercent;
        Level = totals.Level;
        RecommendedCourseId = totals.RecommendedCourseId;
        AiGradingStatus = aiGradingStatus;
    }

    private LevelTestAttempt()
    {
        Answers = [];
        QuestionResults = [];
        SectionScores = [];
        GradingConfig = new LevelTestGradingConfig([], null);
        Level = LevelTestScoring.DEFAULT_LEVEL;
    }

    public Guid Id { get; private set; }

    public Guid QuizId { get; private set; }

    /// <summary>null — анонимная (неклеймленная) попытка.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>Cookie-идентификатор анонима; обязателен, когда <see cref="UserId"/> = null.</summary>
    public Guid? AnonymousId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ClaimedAt { get; private set; }

    public IReadOnlyList<LevelTestAnswer> Answers { get; private set; }

    public IReadOnlyList<LevelTestQuestionResult> QuestionResults { get; private set; }

    public IReadOnlyList<LevelTestSectionScore> SectionScores { get; private set; }

    /// <summary>Снапшот порогов уровней + fallback-курса на момент сабмита (для пересчёта в ST-5).</summary>
    public LevelTestGradingConfig GradingConfig { get; private set; }

    public int OverallPercent { get; private set; }

    public string Level { get; private set; }

    public LevelTestAiGradingStatus AiGradingStatus { get; private set; }

    public Guid? RecommendedCourseId { get; private set; }

    public static Result<LevelTestAttempt, Error> Create(
        Guid quizId,
        Guid? userId,
        Guid? anonymousId,
        IReadOnlyList<LevelTestAnswer> answers,
        IReadOnlyList<LevelTestQuestionKey> questions,
        IReadOnlyList<LevelTestSectionDefinition> sections,
        IReadOnlyList<LevelTestLevelThreshold> thresholds,
        Guid? fallbackCourseId)
    {
        if (quizId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(quizId));
        }

        if (userId is not null && userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (userId is null && (anonymousId is null || anonymousId == Guid.Empty))
        {
            return ProgressErrors.LevelTestAttemptAnonymousIdRequired();
        }

        IReadOnlyList<LevelTestQuestionResult> questionResults =
            LevelTestScoring.GradeQuestions(questions, answers);

        Dictionary<string, LevelTestSectionMeta> sectionMeta = sections
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => new LevelTestSectionMeta(g.First().Title, g.First().Weight, g.First().RecommendedCourseId),
                StringComparer.Ordinal);

        LevelTestTotals totals = LevelTestScoring.ComputeTotals(
            questionResults,
            key => sectionMeta.GetValueOrDefault(
                key,
                new LevelTestSectionMeta(key, LevelTestScoring.DEFAULT_SECTION_WEIGHT, null)),
            thresholds,
            fallbackCourseId,
            includeOpenText: false);

        LevelTestAiGradingStatus status = questionResults.Any(q => q.PendingAi)
            ? LevelTestAiGradingStatus.QUEUED
            : LevelTestAiGradingStatus.NONE;

        return new LevelTestAttempt(
            quizId,
            userId,
            // Аноним-идентификатор имеет смысл только для неклеймленной попытки.
            userId is null ? anonymousId : null,
            answers,
            questionResults,
            new LevelTestGradingConfig(thresholds, fallbackCourseId),
            totals,
            status);
    }

    /// <summary>
    ///     Привязывает анонимную попытку к пользователю (lead-gate: полный разбор —
    ///     после клейма). Повторный клейм тем же пользователем — идемпотентный no-op;
    ///     попытка другого пользователя — конфликт.
    /// </summary>
    public UnitResult<Error> Claim(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (UserId is not null)
        {
            return UserId == userId
                ? UnitResult.Success<Error>()
                : ProgressErrors.LevelTestAttemptAlreadyClaimed();
        }

        UserId = userId;
        ClaimedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Помечает попытку «AI-грейдинг выполняется» (ST-5): поллеры результата видят
    ///     прогресс GRADING вместо QUEUED. Идемпотентен на повторном GRADING (replay
    ///     сообщения после падения mid-flight); из терминальных статусов — конфликт.
    /// </summary>
    public UnitResult<Error> MarkAiGrading()
    {
        if (AiGradingStatus is LevelTestAiGradingStatus.GRADING)
        {
            return UnitResult.Success<Error>();
        }

        if (AiGradingStatus is not LevelTestAiGradingStatus.QUEUED)
        {
            return ProgressErrors.LevelTestAiGradingNotPending();
        }

        AiGradingStatus = LevelTestAiGradingStatus.GRADING;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Применяет AI-оценки открытых ответов (ST-5 только подаёт баллы — пересчёт здесь):
    ///     EarnedPoints = score/100 × MaxPoints, затем секции/overall/уровень/рекомендация
    ///     пересчитываются УЖЕ с open_text в знаменателях, статус → READY. Оценки по
    ///     неизвестным вопросам игнорируются; pending-вопрос без оценки остаётся
    ///     помеченным и даёт 0 заработанных.
    /// </summary>
    public UnitResult<Error> ApplyAiGrades(IReadOnlyDictionary<Guid, LevelTestAiGrade> grades)
    {
        if (AiGradingStatus is not (LevelTestAiGradingStatus.QUEUED
            or LevelTestAiGradingStatus.GRADING
            or LevelTestAiGradingStatus.FAILED))
        {
            return ProgressErrors.LevelTestAiGradingNotPending();
        }

        QuestionResults = QuestionResults
            .Select(q => q.PendingAi && grades.TryGetValue(q.QuestionId, out LevelTestAiGrade? grade)
                ? q.WithAiGrade(grade.Score, grade.Feedback)
                : q)
            .ToList();

        RecomputeTotals(includeOpenText: true);
        AiGradingStatus = LevelTestAiGradingStatus.READY;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     AI-грейдинг не удался: choice-only проценты остаются как есть, pending-ответы
    ///     остаются помеченными, статус → FAILED. Идемпотентен на повторном FAILED.
    /// </summary>
    public UnitResult<Error> MarkAiFailed()
    {
        if (AiGradingStatus is LevelTestAiGradingStatus.FAILED)
        {
            return UnitResult.Success<Error>();
        }

        if (AiGradingStatus is not (LevelTestAiGradingStatus.QUEUED or LevelTestAiGradingStatus.GRADING))
        {
            return ProgressErrors.LevelTestAiGradingNotPending();
        }

        AiGradingStatus = LevelTestAiGradingStatus.FAILED;

        return UnitResult.Success<Error>();
    }

    private void RecomputeTotals(bool includeOpenText)
    {
        Dictionary<string, LevelTestSectionMeta> sectionMeta = SectionScores.ToDictionary(
            s => s.Key,
            s => new LevelTestSectionMeta(s.Title, s.Weight, s.RecommendedCourseId),
            StringComparer.Ordinal);

        LevelTestTotals totals = LevelTestScoring.ComputeTotals(
            QuestionResults,
            key => sectionMeta.GetValueOrDefault(
                key,
                new LevelTestSectionMeta(key, LevelTestScoring.DEFAULT_SECTION_WEIGHT, null)),
            GradingConfig.Thresholds,
            GradingConfig.FallbackCourseId,
            includeOpenText);

        SectionScores = totals.Sections;
        OverallPercent = totals.OverallPercent;
        Level = totals.Level;
        RecommendedCourseId = totals.RecommendedCourseId;
    }
}

/// <summary>AI-оценка одного открытого ответа: score 0..100 + опциональный фидбэк.</summary>
public sealed record LevelTestAiGrade(int Score, string? Feedback);
