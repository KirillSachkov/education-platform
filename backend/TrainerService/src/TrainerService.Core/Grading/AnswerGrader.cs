using TrainerService.Domain;

namespace TrainerService.Core.Grading;

/// <summary>Итог грейдинга одного ответа: вердикт + балл (null для OPEN_TEXT — PENDING).</summary>
public readonly record struct GradeOutcome(AnswerVerdict Verdict, int? ScorePercent);

/// <summary>
///     Детерминированный грейдер ответов DRILL-сессии против снапшотнутого
///     <see cref="GradingKey"/>. Семантика зеркалит ECS/ProgressService
///     <c>QuizAttemptGrader</c>:
///     <list type="bullet">
///         <item>SINGLE_CHOICE / MULTI_CHOICE — верно ⇔ множество выбранных вариантов
///             В ТОЧНОСТИ совпадает с правильным (строго, без partial credit) → 100 или 0.</item>
///         <item>EXACT_TEXT — нормализованное сравнение (только буквы+цифры, lowercase,
///             разделители отброшены) → 100 или 0.</item>
///         <item>OPEN_TEXT (и неизвестные типы) — не автогрейдится: вердикт PENDING,
///             балл null (самопроверка по эталону, mastery не трогается). AI-грейдинг — Ф2.</item>
///     </list>
/// </summary>
public static class AnswerGrader
{
    public const string SINGLE_CHOICE = "SINGLE_CHOICE";
    public const string MULTI_CHOICE = "MULTI_CHOICE";
    public const string EXACT_TEXT = "EXACT_TEXT";
    public const string OPEN_TEXT = "OPEN_TEXT";

    public static GradeOutcome Grade(
        string questionType,
        GradingKey key,
        IReadOnlyList<Guid>? selectedOptionIds,
        string? textAnswer)
    {
        if (IsAutoGradableChoice(questionType))
        {
            bool correct = (selectedOptionIds ?? []).ToHashSet().SetEquals(key.CorrectOptionIds);
            return new GradeOutcome(
                correct ? AnswerVerdict.CORRECT : AnswerVerdict.INCORRECT,
                correct ? 100 : 0);
        }

        if (string.Equals(questionType, EXACT_TEXT, StringComparison.Ordinal))
        {
            bool correct = textAnswer is not null
                && key.ReferenceAnswer is not null
                && string.Equals(
                    NormalizeExactAnswer(textAnswer),
                    NormalizeExactAnswer(key.ReferenceAnswer),
                    StringComparison.Ordinal);
            return new GradeOutcome(
                correct ? AnswerVerdict.CORRECT : AnswerVerdict.INCORRECT,
                correct ? 100 : 0);
        }

        // OPEN_TEXT и неизвестные типы — без авто-грейдинга в Ф1.
        return new GradeOutcome(AnswerVerdict.PENDING, null);
    }

    /// <summary>
    ///     Нормализация для EXACT_TEXT: оставляет только буквы и цифры, к нижнему
    ///     регистру, отбрасывает пробелы/знаки (зеркало ECS EXACT_TEXT-нормализации).
    /// </summary>
    public static string NormalizeExactAnswer(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static bool IsAutoGradableChoice(string questionType) =>
        string.Equals(questionType, SINGLE_CHOICE, StringComparison.Ordinal)
        || string.Equals(questionType, MULTI_CHOICE, StringComparison.Ordinal);

    /// <summary>
    ///     Тип вопроса автогрейдится (есть объективный ключ → балл 0/100): choice или EXACT_TEXT.
    ///     OPEN_TEXT и неизвестные типы — нет (самопроверка, в балл сессии не входят).
    /// </summary>
    public static bool IsAutoGradable(string questionType) =>
        IsAutoGradableChoice(questionType)
        || string.Equals(questionType, EXACT_TEXT, StringComparison.Ordinal);
}
