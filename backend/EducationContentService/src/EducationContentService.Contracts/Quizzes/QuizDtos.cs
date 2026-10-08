namespace EducationContentService.Contracts.Quizzes;

/// <summary>Вариант ответа (без признака правильности — он живёт в author/answer-key проекциях).</summary>
public sealed record QuizOptionDto(Guid Id, string Text);

/// <summary>Порог уровня level-test'а (Level — JUNIOR | MIDDLE | SENIOR).</summary>
public sealed record LevelThresholdDto(string Level, int MinPercent);

/// <summary>Секция level-test'а (Key — kebab-case ключ, матчится с Section вопросов).</summary>
public sealed record LevelTestSectionDto(
    string Key,
    string Title,
    decimal Weight,
    Guid? RecommendedCourseId);

/// <summary>Конфигурация level-test квиза (авторская проекция).</summary>
public sealed record LevelTestConfigDto(
    IReadOnlyList<LevelThresholdDto> LevelThresholds,
    IReadOnlyList<LevelTestSectionDto> Sections,
    Guid? FallbackCourseId);

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
    LevelTestConfigDto? LevelTestConfig,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
///     Студенческая проекция вопроса — БЕЗ правильных ответов и эталона
///     (поля CorrectOptionIds / ReferenceAnswer отсутствуют в контракте намеренно,
///     чтобы они физически не могли утечь в JSON). Section/Difficulty — метаданные
///     для прогресса по секциям level-test'а на фронте.
/// </summary>
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

/// <summary>
///     Ключ ответов вопроса для S2S-грейдинга (ProgressService). Для OPEN_TEXT несёт
///     эталонный ответ — ProgressService раскрывает его студенту ПОСЛЕ сабмита попытки
///     (self-check, full-reveal модель ST-I) — из студенческой проекции он вырезан.
///     Section/Difficulty — метаданные для секционного скоринга level-test'а (ST-4, #477).
///     Text — текст вопроса для AI-грейдинга открытых ответов (ST-5, #480); internal
///     SERVICE/ADMIN endpoint, наружу не утекает.
///     Options — варианты (id+text) для self-contained разбора попытки в ProgressService
///     (#556): чтобы ревью-экран рисовался из result-DTO, а не из «живого» квиза, который
///     мог измениться после сабмита. Nullable + default — старые тест-конструкторы остаются
///     валидными; реальный mapper всегда заполняет.
///     Explanation — разбор «почему так» (#561): ProgressService раскрывает его студенту
///     в ревью-экране ПОСЛЕ сабмита (как ReferenceAnswer); из студенческой проекции вырезан.
/// </summary>
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

/// <summary>
///     Ключ ответов квиза — S2S-контракт для грейдинга попыток в ProgressService.
///     Отдаётся только сервисным/админ-токенам через <c>GET /internal/quizzes/{id}/answer-key</c>.
///     Purpose (MATERIAL_CHECK | LEVEL_TEST) + LevelTestConfig позволяют ProgressService
///     считать уровень и курс-рекомендации для level-test попыток (ST-4, #477).
///     Временный MaterialId (первый ссылающийся материал) снят в ST-13 (#493) —
///     Tier-3 попытки гейтится по самому квизу через <c>AccessType</c>.
/// </summary>
/// <param name="AccessType">
///     Собственный уровень доступа квиза (PUBLIC | REGISTERED | ENROLLED) — для
///     quiz-entitlement в ProgressService (ST-13 #493).
/// </param>
public sealed record QuizAnswerKeyDto(
    Guid QuizId,
    string Purpose,
    int PassingScorePercent,
    IReadOnlyList<QuizAnswerKeyQuestionDto> Questions,
    LevelTestConfigDto? LevelTestConfig,
    string AccessType = "PUBLIC");
