namespace ProgressService.Contracts.Responses;

/// <summary>
///     Лид-гейтнутый тизер результата level-test попытки — всё, что видит аноним
///     (общий процент + уровень). Секции и per-вопрос разбор намеренно отсутствуют
///     в контракте физически (server-side shaping — отдельный тип, не nullable-поля),
///     полный разбор открывается после клейма попытки. Issue #479.
/// </summary>
public sealed record LevelTestAttemptTeaserResponse(
    Guid AttemptId,
    int OverallPercent,
    string Level,
    int TotalQuestions,
    int AnsweredCount,
    string AiGradingStatus);

/// <summary>
///     Полный результат level-test попытки (владелец или админ): секции с процентами
///     и уровнями, per-вопрос разбор (включая AI-поля открытых ответов) и рекомендация
///     курса по самой слабой секции. С #561 per-вопрос разбор раскрывает правильные
///     ответы (варианты + correctOptionIds + эталон + пояснение) — только в ПОЛНОМ
///     результате (владелец/админ после клейма); тизер анонима их не содержит.
/// </summary>
public sealed record LevelTestAttemptResultResponse(
    Guid AttemptId,
    Guid QuizId,
    int OverallPercent,
    string Level,
    string AiGradingStatus,
    Guid? RecommendedCourseId,
    IReadOnlyList<LevelTestSectionScoreResponse> Sections,
    IReadOnlyList<LevelTestQuestionResultResponse> Questions,
    IReadOnlyList<string> WeakestSectionKeys);

public sealed record LevelTestSectionScoreResponse(
    string Key,
    string Title,
    int Percent,
    string Level,
    decimal EarnedPoints,
    decimal MaxPoints);

/// <summary>
///     Per-вопрос разбор: <see cref="IsCorrect"/> — только для choice-вопросов
///     (open_text → null), <see cref="PendingAi"/>/<see cref="AiScore"/>/<see cref="AiFeedback"/> —
///     состояние AI-грейдинга открытого ответа (ST-5).
///     Поля разбора (#561, снапшот попытки — пустые у попыток до деплоя): <see cref="Options"/>
///     (варианты id+text), <see cref="CorrectOptionIds"/>, <see cref="SelectedOptionIds"/>
///     (выбор пользователя), <see cref="TextAnswer"/>/<see cref="ReferenceAnswer"/> для
///     текстовых вопросов, <see cref="Explanation"/> — пояснение «почему так».
/// </summary>
public sealed record LevelTestQuestionResultResponse(
    Guid QuestionId,
    string? Section,
    string? Difficulty,
    string Type,
    bool? IsCorrect,
    bool PendingAi,
    int? AiScore,
    string? AiFeedback,
    decimal EarnedPoints,
    decimal MaxPoints,
    IReadOnlyList<LevelTestOptionResultResponse> Options,
    IReadOnlyList<Guid> CorrectOptionIds,
    IReadOnlyList<Guid> SelectedOptionIds,
    string? TextAnswer,
    string? ReferenceAnswer,
    string? Explanation);

/// <summary>Вариант ответа (id+text) в разборе level-test попытки (#561).</summary>
public sealed record LevelTestOptionResultResponse(Guid Id, string Text);

/// <summary>Итог клейма анонимных попыток: сколько привязано + последняя (для редиректа на результат).</summary>
public sealed record ClaimLevelTestAttemptsResponse(int ClaimedCount, Guid? LatestAttemptId);
