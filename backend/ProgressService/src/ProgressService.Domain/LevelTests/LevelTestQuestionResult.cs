namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Снапшот грейдинга одного вопроса level-test попытки — элемент JSONB-массива
///     <c>level_test_attempts.question_results</c>. Включая AI-поля, которые заполнит
///     ST-5 (<see cref="LevelTestAttempt.ApplyAiGrades"/>) — вторая миграция не нужна.
///     Choice-вопрос: <see cref="IsCorrect"/> true/false, очки полные или 0.
///     OPEN_TEXT: <see cref="IsCorrect"/> = null; отвеченный — <see cref="PendingAi"/> = true
///     и 0 заработанных до AI-грейда; пустой — PendingAi = false (грейдить нечего). Issue #479.
/// </summary>
public sealed record LevelTestQuestionResult(
    Guid QuestionId,
    string? Section,
    string? Difficulty,
    string Type,
    decimal MaxPoints,
    decimal EarnedPoints,
    bool? IsCorrect,
    bool PendingAi,
    int? AiScore,
    string? AiFeedback,
    IReadOnlyList<LevelTestOptionResult>? Options = null,
    IReadOnlyList<Guid>? CorrectOptionIds = null,
    string? ReferenceAnswer = null,
    string? Explanation = null)
{
    public const int MIN_AI_SCORE = 0;
    public const int MAX_AI_SCORE = 100;

    /// <summary>
    ///     Применяет AI-оценку открытого ответа: EarnedPoints = score/100 × MaxPoints,
    ///     PendingAi снимается. Score за пределами 0..100 клампится (выход AI не доверяем).
    /// </summary>
    public LevelTestQuestionResult WithAiGrade(int score, string? feedback)
    {
        int clamped = Math.Clamp(score, MIN_AI_SCORE, MAX_AI_SCORE);

        return this with
        {
            EarnedPoints = MaxPoints * clamped / 100m,
            PendingAi = false,
            AiScore = clamped,
            AiFeedback = feedback,
        };
    }
}

/// <summary>
///     Снапшот варианта ответа (id+text) в разборе level-test попытки (#561) — элемент
///     <see cref="LevelTestQuestionResult.Options"/>. Хранится в JSONB-снапшоте попытки,
///     поэтому разбор с правильными ответами устойчив к правке/удалению квиза.
/// </summary>
public sealed record LevelTestOptionResult(Guid Id, string Text);
