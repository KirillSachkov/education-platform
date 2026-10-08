namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Value Object — вопрос квиза. Хранится элементом упорядоченного JSONB-массива
///     <c>quizzes.questions</c> (порядок в массиве = порядок вопросов). Содержит и
///     варианты ответа, и правильные ответы / эталон — наружу студенту отдаётся
///     только безопасная проекция (без <see cref="CorrectOptionIds"/>,
///     <see cref="ReferenceAnswer"/> и <see cref="Explanation"/>).
/// </summary>
/// <remarks>
///     Инварианты по типам (<see cref="QuizQuestionType"/>):
///     <list type="bullet">
///         <item>SINGLE_CHOICE / MULTI_CHOICE — 2..10 вариантов, уникальные id;
///             SINGLE — ровно один правильный, MULTI — от 1 до числа вариантов;
///             все правильные id обязаны существовать среди вариантов; эталонный
///             ответ запрещён.</item>
///         <item>OPEN_TEXT — без вариантов и правильных id; опциональный эталонный
///             ответ до 4000 символов.</item>
///     </list>
///     <see cref="Explanation"/> (разбор «почему так») опционален и допустим для любого
///     типа — раскрывается студенту только ПОСЛЕ сабмита попытки (в студенческой проекции
///     поля физически нет).
/// </remarks>
public sealed class QuizQuestion
{
    public const int MAX_TEXT_LENGTH = 2000;
    public const int MAX_REFERENCE_ANSWER_LENGTH = 4000;
    public const int MAX_EXPLANATION_LENGTH = 2000;
    public const int MAX_SECTION_LENGTH = 100;
    public const int MIN_OPTIONS = 2;
    public const int MAX_OPTIONS = 10;

    private QuizQuestion(
        Guid id,
        QuizQuestionType type,
        string text,
        IReadOnlyList<QuizOption> options,
        IReadOnlyList<Guid> correctOptionIds,
        string? referenceAnswer,
        string? section,
        QuestionDifficulty? difficulty,
        string? explanation)
    {
        Id = id;
        Type = type;
        Text = text;
        Options = options;
        CorrectOptionIds = correctOptionIds;
        ReferenceAnswer = referenceAnswer;
        Section = section;
        Difficulty = difficulty;
        Explanation = explanation;
    }

    public Guid Id { get; }

    public QuizQuestionType Type { get; }

    public string Text { get; }

    public IReadOnlyList<QuizOption> Options { get; }

    public IReadOnlyList<Guid> CorrectOptionIds { get; }

    /// <summary>Эталонный ответ для OPEN_TEXT (виден только автору и грейдеру).</summary>
    public string? ReferenceAnswer { get; }

    /// <summary>
    ///     Ключ секции level-test'а (kebab-case, например <c>csharp-basics</c>) —
    ///     матчится с <see cref="LevelTestSection.Key"/>. <c>null</c> — вне секций.
    /// </summary>
    public string? Section { get; }

    /// <summary>Сложность вопроса (метаданные level-test скоринга). <c>null</c> — не задана.</summary>
    public QuestionDifficulty? Difficulty { get; }

    /// <summary>
    ///     Пояснение «почему этот ответ правильный» — опционально, допустимо для любого типа.
    ///     Видно только автору и грейдеру; студенту раскрывается в разборе ПОСЛЕ сабмита
    ///     (в студенческой проекции поля нет физически). <c>null</c> — пояснения нет.
    /// </summary>
    public string? Explanation { get; }

    public static Result<QuizQuestion, Error> Create(
        Guid id,
        QuizQuestionType type,
        string text,
        IReadOnlyList<QuizOption> options,
        IReadOnlyList<Guid> correctOptionIds,
        string? referenceAnswer,
        string? section = null,
        QuestionDifficulty? difficulty = null,
        string? explanation = null)
    {
        if (id == Guid.Empty)
            return EducationErrors.QuizQuestionInvalid("у вопроса отсутствует идентификатор");

        if (string.IsNullOrWhiteSpace(text))
            return EducationErrors.QuizQuestionInvalid("текст вопроса не может быть пустым");

        string normalizedText = text.Trim();

        if (normalizedText.Length > MAX_TEXT_LENGTH)
            return EducationErrors.QuizQuestionInvalid($"текст вопроса не может быть длиннее {MAX_TEXT_LENGTH} символов");

        string? normalizedSection = string.IsNullOrWhiteSpace(section) ? null : section.Trim();

        if (normalizedSection is { Length: > MAX_SECTION_LENGTH })
            return EducationErrors.QuizQuestionInvalid($"ключ секции не может быть длиннее {MAX_SECTION_LENGTH} символов");

        string? normalizedExplanation = string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();

        if (normalizedExplanation is { Length: > MAX_EXPLANATION_LENGTH })
            return EducationErrors.QuizQuestionInvalid($"пояснение не может быть длиннее {MAX_EXPLANATION_LENGTH} символов");

        return type switch
        {
            QuizQuestionType.SINGLE_CHOICE or QuizQuestionType.MULTI_CHOICE =>
                CreateChoice(id, type, normalizedText, options, correctOptionIds, referenceAnswer, normalizedSection, difficulty, normalizedExplanation),
            QuizQuestionType.OPEN_TEXT =>
                CreateOpenText(id, normalizedText, options, correctOptionIds, referenceAnswer, normalizedSection, difficulty, normalizedExplanation),
            QuizQuestionType.EXACT_TEXT =>
                CreateExactText(id, normalizedText, options, correctOptionIds, referenceAnswer, normalizedSection, difficulty, normalizedExplanation),
            _ => EducationErrors.QuizQuestionInvalid($"неизвестный тип вопроса: {type}"),
        };
    }

    private static Result<QuizQuestion, Error> CreateChoice(
        Guid id,
        QuizQuestionType type,
        string text,
        IReadOnlyList<QuizOption> options,
        IReadOnlyList<Guid> correctOptionIds,
        string? referenceAnswer,
        string? section,
        QuestionDifficulty? difficulty,
        string? explanation)
    {
        if (!string.IsNullOrWhiteSpace(referenceAnswer))
            return EducationErrors.QuizQuestionInvalid("эталонный ответ допустим только для вопросов с открытым ответом");

        if (options.Count is < MIN_OPTIONS or > MAX_OPTIONS)
            return EducationErrors.QuizOptionsCountInvalid(MIN_OPTIONS, MAX_OPTIONS);

        HashSet<Guid> optionIds = options.Select(o => o.Id).ToHashSet();
        if (optionIds.Count != options.Count)
            return EducationErrors.QuizQuestionInvalid("идентификаторы вариантов ответа должны быть уникальными");

        List<Guid> distinctCorrect = correctOptionIds.Distinct().ToList();
        if (distinctCorrect.Count != correctOptionIds.Count)
            return EducationErrors.QuizCorrectOptionsInvalid("правильные варианты не должны повторяться");

        if (distinctCorrect.Any(c => !optionIds.Contains(c)))
            return EducationErrors.QuizCorrectOptionsInvalid("правильный вариант должен ссылаться на существующий вариант ответа");

        if (type == QuizQuestionType.SINGLE_CHOICE && distinctCorrect.Count != 1)
            return EducationErrors.QuizCorrectOptionsInvalid("вопрос с одним ответом должен иметь ровно один правильный вариант");

        if (type == QuizQuestionType.MULTI_CHOICE && distinctCorrect.Count < 1)
            return EducationErrors.QuizCorrectOptionsInvalid("вопрос с несколькими ответами должен иметь хотя бы один правильный вариант");

        return new QuizQuestion(id, type, text, [.. options], distinctCorrect, referenceAnswer: null, section, difficulty, explanation);
    }

    private static Result<QuizQuestion, Error> CreateOpenText(
        Guid id,
        string text,
        IReadOnlyList<QuizOption> options,
        IReadOnlyList<Guid> correctOptionIds,
        string? referenceAnswer,
        string? section,
        QuestionDifficulty? difficulty,
        string? explanation)
    {
        if (options.Count > 0 || correctOptionIds.Count > 0)
            return EducationErrors.QuizQuestionInvalid("вопрос с открытым ответом не может содержать варианты ответа");

        string? normalizedReference = string.IsNullOrWhiteSpace(referenceAnswer)
            ? null
            : referenceAnswer.Trim();

        if (normalizedReference is { Length: > MAX_REFERENCE_ANSWER_LENGTH })
            return EducationErrors.QuizQuestionInvalid($"эталонный ответ не может быть длиннее {MAX_REFERENCE_ANSWER_LENGTH} символов");

        return new QuizQuestion(id, QuizQuestionType.OPEN_TEXT, text, [], [], normalizedReference, section, difficulty, explanation);
    }

    private static Result<QuizQuestion, Error> CreateExactText(
        Guid id,
        string text,
        IReadOnlyList<QuizOption> options,
        IReadOnlyList<Guid> correctOptionIds,
        string? referenceAnswer,
        string? section,
        QuestionDifficulty? difficulty,
        string? explanation)
    {
        if (options.Count > 0 || correctOptionIds.Count > 0)
            return EducationErrors.QuizQuestionInvalid("вопрос с точным ответом не может содержать варианты ответа");

        if (string.IsNullOrWhiteSpace(referenceAnswer))
            return EducationErrors.QuizQuestionInvalid("вопрос с точным ответом обязан иметь эталонный ответ");

        string normalizedReference = referenceAnswer.Trim();

        if (normalizedReference.Length > MAX_REFERENCE_ANSWER_LENGTH)
            return EducationErrors.QuizQuestionInvalid($"эталонный ответ не может быть длиннее {MAX_REFERENCE_ANSWER_LENGTH} символов");

        return new QuizQuestion(id, QuizQuestionType.EXACT_TEXT, text, [], [], normalizedReference, section, difficulty, explanation);
    }
}
