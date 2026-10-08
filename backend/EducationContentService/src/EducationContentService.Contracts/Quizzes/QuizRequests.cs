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

/// <summary>
///     Вопрос в запросе создания/обновления квиза. Порядок вопросов в массиве — порядок показа.
/// </summary>
/// <param name="Id">Идентификатор вопроса. Если не задан — сервер сгенерирует.</param>
/// <param name="Type">Тип вопроса (SINGLE_CHOICE | MULTI_CHOICE | OPEN_TEXT).</param>
/// <param name="Text">Текст вопроса (до 2000 символов).</param>
/// <param name="Options">Варианты ответа (2..10 для choice-типов; пусто для OPEN_TEXT).</param>
/// <param name="CorrectOptionIds">
///     Id правильных вариантов (SINGLE_CHOICE — ровно один, MULTI_CHOICE — 1..кол-во вариантов;
///     пусто для OPEN_TEXT).
/// </param>
/// <param name="ReferenceAnswer">Эталонный ответ (только OPEN_TEXT, до 4000 символов).</param>
/// <param name="Section">Ключ секции level-test'а (kebab-case, например csharp-basics; до 100 символов).</param>
/// <param name="Difficulty">Сложность вопроса (JUNIOR | MIDDLE | SENIOR).</param>
/// <param name="Explanation">Пояснение «почему так» (до 2000 символов; любой тип вопроса; раскрывается студенту после сабмита).</param>
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

/// <summary>Порог уровня в конфигурации level-test'а.</summary>
/// <param name="Level">Уровень (JUNIOR | MIDDLE | SENIOR).</param>
/// <param name="MinPercent">Минимальный процент правильных ответов (0..100).</param>
public sealed record LevelThresholdRequest(string Level, int MinPercent);

/// <summary>Секция в конфигурации level-test'а.</summary>
/// <param name="Key">Kebab-case ключ секции (матчится с Section вопросов).</param>
/// <param name="Title">Название секции.</param>
/// <param name="Weight">Вес секции в скоринге (положительный, дефолт 1.0).</param>
/// <param name="RecommendedCourseId">Курс-рекомендация при слабом результате по секции.</param>
public sealed record LevelTestSectionRequest(
    string Key,
    string Title,
    decimal Weight = 1.0m,
    Guid? RecommendedCourseId = null);

/// <summary>Конфигурация level-test квиза (создание и обновление — replace целиком).</summary>
/// <param name="LevelThresholds">Пороги уровней (непустой набор).</param>
/// <param name="Sections">Секции (ключи уникальны; допустимо пусто).</param>
/// <param name="FallbackCourseId">Курс-рекомендация по умолчанию.</param>
public sealed record LevelTestConfigRequest(
    IReadOnlyList<LevelThresholdRequest> LevelThresholds,
    IReadOnlyList<LevelTestSectionRequest>? Sections = null,
    Guid? FallbackCourseId = null);

/// <summary>
///     Запрос на создание квиза (создаётся в DRAFT, standalone — #489).
///     Привязка к материалу — через <c>PATCH /materials/{id}</c> (UpdateMaterialRequest.QuizId).
/// </summary>
/// <param name="Title">Заголовок.</param>
/// <param name="Questions">Упорядоченный набор вопросов (до 50). Пусто — допустимо для черновика.</param>
/// <param name="PassingScorePercent">Проходной балл в процентах (0..100, дефолт 70).</param>
/// <param name="Purpose">
///     Назначение квиза (MATERIAL_CHECK | LEVEL_TEST). <c>null</c> — MATERIAL_CHECK.
///     Immutable после создания.
/// </param>
/// <param name="LevelTestConfig">Конфигурация level-test'а (опциональна).</param>
/// <param name="AccessType">
///     Уровень доступа квиза (PUBLIC | REGISTERED | ENROLLED). <c>null</c> — PUBLIC.
/// </param>
/// <param name="AuthorId">
///     Admin-only override автора (MCP/service-token сценарий — у client_credentials
///     caller'а sub=client_id → UserId=Guid.Empty). Для не-admin caller'а игнорируется.
///     Зеркало <see cref="Materials.CreateMaterialRequest"/>.
/// </param>
public sealed record CreateQuizRequest(
    string Title,
    IReadOnlyList<QuizQuestionRequest>? Questions = null,
    int PassingScorePercent = 70,
    string? Purpose = null,
    LevelTestConfigRequest? LevelTestConfig = null,
    string? AccessType = null,
    Guid? AuthorId = null);

/// <summary>
///     Запрос на обновление квиза. Заменяет весь набор вопросов и level-test
///     конфигурацию целиком (MVP-контракт); <see cref="CreateQuizRequest.Purpose"/> immutable.
/// </summary>
/// <param name="Title">Новый заголовок.</param>
/// <param name="Questions">Новый полный упорядоченный набор вопросов (до 50).</param>
/// <param name="PassingScorePercent">Проходной балл в процентах (0..100).</param>
/// <param name="LevelTestConfig">Новая конфигурация level-test'а (<c>null</c> — очистить).</param>
/// <param name="AccessType">
///     Новый уровень доступа квиза (PUBLIC | REGISTERED | ENROLLED). <c>null</c> — не менять.
/// </param>
public sealed record UpdateQuizRequest(
    string Title,
    IReadOnlyList<QuizQuestionRequest>? Questions = null,
    int PassingScorePercent = 70,
    LevelTestConfigRequest? LevelTestConfig = null,
    string? AccessType = null);
