namespace EducationContentService.Domain.Quizzes;

/// <summary>
///     Сложность вопроса квиза.
///     Сериализуется строкой (UPPER_SNAKE_CASE) в JSONB и API.
/// </summary>
public enum QuestionDifficulty
{
    /// <summary>Базовый уровень.</summary>
    JUNIOR,

    /// <summary>Средний уровень.</summary>
    MIDDLE,

    /// <summary>Продвинутый уровень.</summary>
    SENIOR
}