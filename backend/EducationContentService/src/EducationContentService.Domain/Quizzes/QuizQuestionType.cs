namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Тип вопроса квиза. Сериализуется строкой (UPPER_SNAKE_CASE) в JSONB и API.
/// </summary>
public enum QuizQuestionType
{
    /// <summary>Один правильный вариант из списка.</summary>
    SINGLE_CHOICE,

    /// <summary>Несколько правильных вариантов из списка.</summary>
    MULTI_CHOICE,

    /// <summary>Свободный текстовый ответ (сверяется с эталоном вручную/AI).</summary>
    OPEN_TEXT,

    /// <summary>
    ///     Точный текстовый ответ (#528): рукописный ввод без вариантов-подсказок
    ///     (например, «что выведет код»). Грейдится детерминированно — нормализованное
    ///     сравнение с эталоном из <see cref="QuizQuestion.ReferenceAnswer"/>.
    /// </summary>
    EXACT_TEXT
}
