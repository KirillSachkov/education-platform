namespace ProgressService.Domain.LevelTests;

/// <summary>
///     Value Object — ответ респондента на один вопрос level-test'а. Хранится элементом
///     JSONB-массива <c>level_test_attempts.answers</c>: для choice-вопросов — выбранные
///     варианты, для открытых — свободный текст. Зеркало <c>QuizAttemptAnswer</c>, но
///     отдельный тип: level-test — самостоятельный агрегат с другой access-моделью
///     (анонимные попытки + lead-gate), контракты не должны сцепляться. Issue #479.
/// </summary>
public sealed class LevelTestAnswer
{
    public const int MAX_TEXT_ANSWER_LENGTH = 4000;
    public const int MAX_SELECTED_OPTIONS = 20;

    private LevelTestAnswer(Guid questionId, IReadOnlyList<Guid> selectedOptionIds, string? textAnswer)
    {
        QuestionId = questionId;
        SelectedOptionIds = selectedOptionIds;
        TextAnswer = textAnswer;
    }

    public Guid QuestionId { get; }

    public IReadOnlyList<Guid> SelectedOptionIds { get; }

    public string? TextAnswer { get; }

    /// <summary>Есть ли в ответе содержимое (выбранные варианты или непустой текст).</summary>
    public bool HasContent => SelectedOptionIds.Count > 0 || TextAnswer is not null;

    public static Result<LevelTestAnswer, Error> Create(
        Guid questionId,
        IReadOnlyList<Guid>? selectedOptionIds,
        string? textAnswer)
    {
        if (questionId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(questionId));
        }

        List<Guid> normalizedOptions = (selectedOptionIds ?? []).Distinct().ToList();
        if (normalizedOptions.Count > MAX_SELECTED_OPTIONS)
        {
            return ProgressErrors.QuizAttemptTooManySelectedOptions(MAX_SELECTED_OPTIONS);
        }

        string? normalizedText = string.IsNullOrWhiteSpace(textAnswer) ? null : textAnswer.Trim();
        if (normalizedText is { Length: > MAX_TEXT_ANSWER_LENGTH })
        {
            return ProgressErrors.QuizAttemptTextAnswerTooLong(MAX_TEXT_ANSWER_LENGTH);
        }

        return new LevelTestAnswer(questionId, normalizedOptions, normalizedText);
    }
}
