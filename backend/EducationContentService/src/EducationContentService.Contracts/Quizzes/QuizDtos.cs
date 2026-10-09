namespace EducationContentService.Contracts.Quizzes;

/// <summary>Вариант ответа (без признака правильности — он живёт в author/answer-key проекциях).</summary>
public sealed record QuizOptionDto(Guid Id, string Text);

/// <summary>
///     Полная авторская проекция вопроса — ВКЛЮЧАЕТ правильные ответы и эталон.
///     Отдаётся только владельцу (ownership-checked) через <c>GET /quizzes/{id}</c>.
/// </summary>
public sealed record QuizQuestionAuthorDto(
    Guid Id,
    string Type,
    string Text,
    string? Section,
    string? Difficulty,
    IReadOnlyList<QuizOptionDto> Options,
    IReadOnlyList<Guid> CorrectOptionIds,
    string? ReferenceAnswer,
    string? Explanation = null);

/// <summary>
///     Полная авторская проекция квиза (включая ответы). Квиз — standalone-сущность (#489):
///     привязка к материалам живёт на стороне материалов (<c>materials.quiz_id</c>),
///     поэтому MaterialId в проекции нет. AccessType — собственный уровень доступа квиза
///     (PUBLIC | REGISTERED | ENROLLED, зеркало Material).
/// </summary>
public sealed record QuizAuthorDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string Status,
    string AccessType,
    string Purpose,
    int PassingScorePercent,
    IReadOnlyList<QuizQuestionAuthorDto> Questions,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Студенческая проекция вопроса без правильных ответов, эталона и пояснения.</summary>
public sealed record QuizQuestionStudentDto(
    Guid Id,
    string Type,
    string Text,
    string? Section,
    string? Difficulty,
    IReadOnlyList<QuizOptionDto> Options);

/// <summary>
///     Студенческая проекция опубликованного квиза.
/// </summary>
/// <param name="MaterialId">
///     Материал-контекст, через который квиз был запрошен
///     (<c>GET /materials/{id}/quiz</c>); <c>null</c> для standalone-чтения
///     <c>GET /quizzes/{id}/student</c> (#490).
/// </param>
public sealed record QuizStudentDto(
    Guid Id,
    Guid? MaterialId,
    string Title,
    int PassingScorePercent,
    IReadOnlyList<QuizQuestionStudentDto> Questions);

/// <summary>S2S-ключ ответов. Ответы и пояснение раскрываются студенту после отправки попытки.
///     Варианты ответа сохраняют самостоятельный разбор попытки при изменении квиза.</summary>
public sealed record QuizAnswerKeyQuestionDto(
    Guid Id,
    string Type,
    string Text,
    string? Section,
    string? Difficulty,
    IReadOnlyList<Guid> CorrectOptionIds,
    string? ReferenceAnswer,
    IReadOnlyList<QuizOptionDto>? Options = null,
    string? Explanation = null);

/// <summary>S2S-ключ ответов для ProgressService. Доступен только SERVICE/ADMIN.
///     AccessType определяет проверку доступа к квизу.</summary>
public sealed record QuizAnswerKeyDto(
    Guid QuizId,
    string Purpose,
    int PassingScorePercent,
    IReadOnlyList<QuizAnswerKeyQuestionDto> Questions,
    string AccessType = "PUBLIC");