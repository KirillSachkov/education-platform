namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Value Object — вариант ответа вопроса квиза. Хранится внутри JSONB-массива
///     <c>quizzes.questions</c> (вложен в <see cref="QuizQuestion"/>).
/// </summary>
public sealed record QuizOption
{
    public const int MAX_TEXT_LENGTH = 500;

    private QuizOption(Guid id, string text)
    {
        Id = id;
        Text = text;
    }

    public Guid Id { get; }

    public string Text { get; }

    public static Result<QuizOption, Error> Create(Guid id, string text)
    {
        if (id == Guid.Empty)
            return EducationErrors.QuizOptionInvalid("у варианта ответа отсутствует идентификатор");

        if (string.IsNullOrWhiteSpace(text))
            return EducationErrors.QuizOptionInvalid("текст варианта ответа не может быть пустым");

        string normalized = text.Trim();

        if (normalized.Length > MAX_TEXT_LENGTH)
            return EducationErrors.QuizOptionInvalid($"текст варианта ответа не может быть длиннее {MAX_TEXT_LENGTH} символов");

        return new QuizOption(id, normalized);
    }
}
