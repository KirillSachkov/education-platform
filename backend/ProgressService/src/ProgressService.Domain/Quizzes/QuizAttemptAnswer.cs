namespace ProgressService.Domain.Quizzes;

/// <summary>
///     Value Object — ответ студента на один вопрос квиза. Хранится элементом
///     JSONB-массива <c>quiz_attempts.answers</c>: для choice-вопросов —
///     выбранные варианты, для открытых — свободный текст. Признак правильности
///     НЕ персистится — он вычисляется заново против answer-key на каждом чтении.
/// </summary>
public sealed class QuizAttemptAnswer
{
    public const int MAX_TEXT_ANSWER_LENGTH = 4000;
    public const int MAX_SELECTED_OPTIONS = 20;

    private QuizAttemptAnswer(Guid questionId, IReadOnlyList<Guid> selectedOptionIds, string? textAnswer)
    {
        QuestionId = questionId;
        SelectedOptionIds = selectedOptionIds;
        TextAnswer = textAnswer;
    }

    public Guid QuestionId { get; }

    public IReadOnlyList<Guid> SelectedOptionIds { get; }

    public string? TextAnswer { get; }

    public static Result<QuizAttemptAnswer, Error> Create(
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

        return new QuizAttemptAnswer(questionId, normalizedOptions, normalizedText);
    }
}
