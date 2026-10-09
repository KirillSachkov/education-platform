namespace EducationContentService.Contracts.Quizzes;

/// <summary>
///     Вариант ответа в запросе создания/обновления квиза.
/// </summary>
/// <param name="Id">
///     Идентификатор варианта. Если не задан — сервер сгенерирует, но тогда на вариант
///     нельзя сослаться из <see cref="QuizQuestionRequest.CorrectOptionIds"/> (UI обычно
///     генерирует UUID сам).
/// </param>
/// <param name="Text">Текст варианта (до 500 символов).</param>
public sealed record QuizOptionRequest(Guid? Id, string Text);

/// <summary>Вопрос квиза. Порядок в массиве определяет порядок показа.
///     Section и Difficulty — необязательные метаданные; Explanation раскрывается после отправки.</summary>
public sealed record QuizQuestionRequest(
    Guid? Id,
    string Type,
    string Text,
    IReadOnlyList<QuizOptionRequest>? Options = null,
    IReadOnlyList<Guid>? CorrectOptionIds = null,
    string? ReferenceAnswer = null,
    string? Section = null,
    string? Difficulty = null,
    string? Explanation = null);

/// <summary>Создаёт standalone-квиз в DRAFT. Материалы ссылаются на него через QuizId.
///     Purpose по умолчанию MATERIAL_CHECK. AuthorId разрешён только для ADMIN.</summary>
public sealed record CreateQuizRequest(
    string Title,
    IReadOnlyList<QuizQuestionRequest>? Questions = null,
    int PassingScorePercent = 70,
    string? Purpose = null,
    string? AccessType = null,
    Guid? AuthorId = null);

/// <summary>Обновляет заголовок, проходной балл и весь набор вопросов квиза.
///     AccessType = null сохраняет текущий доступ; Purpose остаётся неизменным.</summary>
public sealed record UpdateQuizRequest(
    string Title,
    IReadOnlyList<QuizQuestionRequest>? Questions = null,
    int PassingScorePercent = 70,
    string? AccessType = null);