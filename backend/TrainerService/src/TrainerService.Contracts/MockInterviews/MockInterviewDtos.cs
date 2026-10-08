namespace TrainerService.Contracts.MockInterviews;

/// <summary>
///     Краткая карточка симуляции собеседования в списке (метаданные, без вопросов).
///     <see cref="QuestionCount"/> — РЕАЛЬНО доступное число вопросов на сессию: курированный набор
///     считает только резолвимые ссылки (висячие на удалённые вопросы не в счёт), legacy-симуляция —
///     вопросы в банках её тем (capped). <c>0</c> ⇒ старт заблокирован, карточка показывает
///     «вопросы готовятся» вместо ошибки после клика. #585/#623.
/// </summary>
public sealed record MockInterviewSummaryDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    int TopicCount,
    int QuestionCount);

/// <summary>
///     Запрос на старт MOCK-сессии из симуляции собеседования. Вопросы набираются из банков
///     тем симуляции (любого назначения — STUDY и MOCK), шафлятся, берётся min(N, доступных).
/// </summary>
/// <param name="QuestionCount">Сколько вопросов в сессии (1..50). Пусто = 15.</param>
/// <param name="TimeLimitSeconds">Опциональный лимит времени на сессию (информативный таймер).</param>
public sealed record StartMockInterviewRequest(
    int? QuestionCount,
    int? TimeLimitSeconds);

/// <summary>Запрос на создание симуляции собеседования (admin).</summary>
public sealed record CreateMockInterviewRequest(
    string Slug,
    string Title,
    string? Description,
    IReadOnlyList<Guid> TopicIds,
    int? SortIndex);

/// <summary>Запрос на смену назначения банка (admin): STUDY | MOCK.</summary>
public sealed record SetBankPurposeRequest(string Purpose);

/// <summary>Id созданной симуляции собеседования.</summary>
public sealed record MockInterviewIdResponse(Guid Id);

/// <summary>Ссылка на конкретный вопрос локального банка тренажёра в курированном наборе мок-собеса (#623).</summary>
public sealed record MockInterviewQuestionRefDto(Guid QuestionId);

/// <summary>
///     Запрос на обновление симуляции собеседования (admin): название/описание, размер случайной
///     подвыборки на сессию и курированный набор вопросов. <see cref="QuestionsPerSession"/> = null →
///     показывать весь набор. #585.
/// </summary>
public sealed record UpdateMockInterviewRequest(
    string Title,
    string? Description,
    int? QuestionsPerSession,
    IReadOnlyList<MockInterviewQuestionRefDto> Questions);

/// <summary>
///     Один вопрос курированного набора в редакторе автора: ссылка (question) + резолвнутые из
///     локального банка метаданные (стем/тип/сложность) + тема-источник (банк → тема). #585/#623.
/// </summary>
public sealed record MockInterviewBuilderQuestionDto(
    Guid QuestionId,
    string Text,
    string Type,
    string? Difficulty,
    Guid? TopicId,
    string? TopicTitle);

/// <summary>
///     Детальная карточка симуляции для редактора автора: метаданные + размер подвыборки + курированный
///     набор вопросов (с резолвнутыми стемами, в порядке SortIndex). Висячие ссылки на удалённые квизы/
///     вопросы пропущены. #585.
/// </summary>
public sealed record MockInterviewBuilderDto(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    bool IsPublished,
    int? QuestionsPerSession,
    IReadOnlyList<MockInterviewBuilderQuestionDto> Questions);

/// <summary>Карточка симуляции в авторском списке (включая DRAFT): метаданные + размеры набора. #585.</summary>
public sealed record MockInterviewManageItemDto(
    Guid Id,
    string Slug,
    string Title,
    bool IsPublished,
    int QuestionCount,
    int? QuestionsPerSession);

/// <summary>
///     Один доступный для выбора вопрос в пикере банка (источник курированного набора): ссылка
///     (question) + метаданные + банк/тема/трек-источник (для группировки в UI). #585/#623.
/// </summary>
public sealed record QuestionBankItemDto(
    Guid QuestionId,
    string Text,
    string Type,
    string? Difficulty,
    Guid BankId,
    Guid TopicId,
    string TopicTitle,
    Guid TrackId,
    string TrackTitle);
