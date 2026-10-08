namespace TrainerService.Contracts.Sessions;

/// <summary>Вариант ответа в снапшоте вопроса сессии (id + текст, без признака правильности).</summary>
public sealed record SessionOptionDto(Guid Id, string Text);

/// <summary>Запрос на старт DRILL/тест-сессии по теме.</summary>
/// <param name="RevealPolicy">
///     Когда раскрывается правильный ответ: <c>END_OF_SESSION</c> (default — счёт копится, разбор
///     после Complete) | <c>PER_QUESTION</c> (мгновенный фидбэк). Пусто = END_OF_SESSION (#568 Ф2).
/// </param>
public sealed record StartSessionRequest(
    string? Mode,
    Guid TopicId,
    int? QuestionCount,
    string? RevealPolicy = null,
    IReadOnlyList<Guid>? QuestionIds = null);

/// <summary>
///     Запрос на старт LEARN-сессии («обучение по тестам», #568 Ф2): адаптивный мини-тест с
///     мгновенным фидбэком на каждый вопрос; ошибочные повторяются до усвоения. Formative —
///     без записываемого балла на пользователе; питает <c>QuestionStudyState</c> + SRS.
/// </summary>
public sealed record StartLearnSessionRequest(
    Guid TopicId,
    int? QuestionCount);

/// <summary>
///     Запрос на старт REVIEW-сессии (#568 Ф2): тест по ПРОИЗВОЛЬНОМУ набору вопросов
///     («Доучить» по ошибкам / «Пройти тест по закладке»). Собирает LEARN-сессию
///     (<c>PER_QUESTION</c>, instant feedback) ровно из переданных вопросов — каждый резолвится
///     до своего quiz/topic/grading-key через доступные пользователю STUDY-банки. Недоступные /
///     несуществующие вопросы отбрасываются; порядок сохраняется; набор клампится до 50.
/// </summary>
public sealed record StartReviewSessionRequest(
    IReadOnlyList<Guid> QuestionIds);

/// <summary>
///     Запрос на старт MOCK-сессии (симуляция собеса по треку): вопросы набираются
///     кросс-тематически из доступных банков трека, шафлятся, берётся min(N, доступных).
/// </summary>
/// <param name="TrackId">Трек, по которому собирается симуляция.</param>
/// <param name="QuestionCount">Сколько вопросов в сессии (1..50).</param>
/// <param name="TimeLimitSeconds">Опциональный лимит времени на сессию (информативный таймер).</param>
/// <param name="Difficulty">Опциональный фильтр сложности вопросов (JUNIOR/MIDDLE/SENIOR).</param>
/// <param name="Direction">
///     Опциональный scope по направлению трека (BACKEND/FRONTEND/FULLSTACK/GENERAL, #568 Ф2):
///     пул сужается до тем этого направления. Пусто = весь трек.
/// </param>
public sealed record StartMockSessionRequest(
    Guid TrackId,
    int? QuestionCount,
    int? TimeLimitSeconds,
    string? Difficulty,
    string? Direction = null);

/// <summary>Запрос на мгновенную проверку одного ответа в сессии.</summary>
/// <param name="OptionIds">Выбранные варианты (для SINGLE/MULTI_CHOICE).</param>
/// <param name="Text">Текст ответа (для EXACT_TEXT / OPEN_TEXT).</param>
public sealed record CheckAnswerRequest(
    IReadOnlyList<Guid>? OptionIds,
    string? Text);

/// <summary>
///     Снапшот вопроса сессии — студенческая проекция. <b>Без ключа грейдинга</b>.
///     Если вопрос уже отвечен (<see cref="IsAnswered"/>), несёт результат проверки
///     для ревью: вердикт/балл/фидбэк/разбор + правильный ответ. Для неотвеченного
///     вопроса все «answer/correct»-поля = null.
/// </summary>
public sealed record SessionItemDto(
    Guid Id,
    Guid QuestionId,
    Guid TopicId,
    string QuestionType,
    // Nullable (#674): nulled out (with Options) when the item is locked for a non-PRO caller (redaction).
    string? QuestionText,
    IReadOnlyList<SessionOptionDto> Options,
    string? Section,
    // Уровень вопроса (JUNIOR/MIDDLE/SENIOR) — отдаётся ВСЕГДА (для бейджа уровня), даже до ответа.
    string? Difficulty,
    int SortIndex,
    bool IsAnswered,
    string? AnswerRaw,
    int? ScorePercent,
    string? Verdict,
    string? Feedback,
    // Раскрываются ТОЛЬКО для уже отвеченного вопроса (review). Снимаются из снапшота
    // grading-key, никогда не отдаются для неотвеченного item'а.
    IReadOnlyList<Guid>? CorrectOptionIds,
    string? ReferenceAnswer,
    string? Explanation,
    // Монетизация по типу вопроса (#623): OPEN_TEXT (развёрнутый/голосовой ответ → AI-анализ)
    // заблокирован для не-PRO ("🔒 Доступно на полном доступе"). Закрытые вопросы (choice/exact)
    // никогда не locked — их free-юзер проходит без лимита. LockReason = "pro_required" при IsLocked.
    bool IsLocked = false,
    string? LockReason = null);

/// <summary>
///     Тренировочная сессия — студенческая проекция. Items НЕ содержат ключ грейдинга
///     (правильные ответы раскрываются на item'е лишь после ответа на него).
/// </summary>
/// <param name="GradingStatus">
///     Статус AI-грейдинга открытых ответов (#585): <c>NOT_REQUIRED</c> | <c>PENDING</c> |
///     <c>GRADING</c> | <c>GRADED</c> | <c>FAILED</c>. Фронт поллит результаты мок-собеса до GRADED.
/// </param>
/// <param name="AiOverallFeedback">Итоговый AI-фидбэк по мок-собесу. Null до GRADED.</param>
/// <param name="AiWeakTopics">Слабые темы из AI-разбора (десериализованы из JSON). Пусто до GRADED.</param>
/// <param name="AiStrengths">Сильные стороны из AI-разбора. Пусто до GRADED.</param>
public sealed record SessionDto(
    Guid Id,
    string Mode,
    string Status,
    // Когда раскрывается правильный ответ: END_OF_SESSION (grade-at-end) | PER_QUESTION (instant).
    // Фронт по нему решает, показывать ли разбор сразу после CheckAnswer (#568 Ф2).
    string RevealPolicy,
    IReadOnlyList<Guid> TopicIds,
    int? TimeLimitSeconds,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? ScorePercent,
    IReadOnlyList<SessionItemDto> Items,
    string GradingStatus,
    string? AiOverallFeedback,
    IReadOnlyList<string> AiWeakTopics,
    IReadOnlyList<string> AiStrengths);

/// <summary>Результат мгновенной проверки одного ответа.</summary>
/// <param name="ScorePercent">0..100, либо null для OPEN_TEXT когда оценка скрыта/недоступна (PENDING).</param>
/// <param name="CorrectOptionIds">Правильные варианты (для choice-вопросов).</param>
/// <param name="ReferenceAnswer">Эталонный ответ (для текстовых вопросов).</param>
/// <param name="Feedback">
///     Короткий AI-фидбэк по открытому ответу (OPEN_TEXT, не-мок инлайн-грейдинг #568 W2). Null для
///     авто-грейдимых вопросов и для скрытого (END_OF_SESSION) раскрытия.
/// </param>
/// <param name="AnswerText">
///     Записанный ответ студента (для OPEN_TEXT — печатный текст ИЛИ распознанная из голоса речь, #585).
///     Раскрывается только под PER_QUESTION, чтобы показать «Твой ответ» в разборе — иначе у голосового
///     ответа на клиенте нет текста (транскрипт живёт на сервере). Null для choice/exact (у клиента свой
///     черновик) и для скрытого (END_OF_SESSION) раскрытия.
/// </param>
public sealed record CheckAnswerResponse(
    Guid ItemId,
    string Verdict,
    int? ScorePercent,
    IReadOnlyList<Guid>? CorrectOptionIds,
    string? ReferenceAnswer,
    string? Explanation,
    string? Feedback = null,
    string? AnswerText = null);

/// <summary>Итог завершённой сессии.</summary>
/// <param name="GradingStatus">
///     Статус AI-грейдинга открытых ответов (#585): <c>NOT_REQUIRED</c> для авто-грейдимых сессий;
///     <c>PENDING</c> сразу после Complete мок-собеса с открытыми ответами (фронт поллит результаты).
/// </param>
public sealed record SessionSummaryDto(
    Guid Id,
    string Status,
    int ScorePercent,
    int TotalItems,
    int AnsweredItems,
    int CorrectItems,
    DateTime? CompletedAt,
    string GradingStatus);

/// <summary>
///     Краткая запись сессии в истории вызывающего («вернуться к сессии» / история).
///     Без item'ов и ключа грейдинга.
/// </summary>
/// <param name="TimeLimitSeconds">
///     Лимит времени MOCK-сессии (информативный таймер). Фронт детектит истёкший MOCK
///     (прошло больше <c>TimeLimitSeconds</c> с <see cref="StartedAt"/>) → показывает «результаты»,
///     а не «продолжить». Для не-MOCK режимов null (#568).
/// </param>
public sealed record SessionHistoryItemDto(
    Guid Id,
    string Mode,
    string Status,
    IReadOnlyList<Guid> TopicIds,
    int? ScorePercent,
    int AnsweredCount,
    int TotalCount,
    DateTime StartedAt,
    DateTime? CompletedAt,
    int? TimeLimitSeconds,
    // Статус AI-грейдинга (#585): NOT_REQUIRED | PENDING | GRADING | GRADED | FAILED. Позволяет
    // истории показать «ИИ проверяет…» для мок-симуляции, грейдящейся в фоне (#568), без захода
    // в саму сессию. Только статус — без item'ов и ключа грейдинга.
    string GradingStatus);

/// <summary>Доля верных ответов по одному срезу (сложность или тема).</summary>
/// <param name="Key">Идентификатор среза: difficulty-литерал (JUNIOR/...) либо topicId.</param>
/// <param name="Correct">Сколько авто-грейдимых ответов верны.</param>
/// <param name="Graded">Сколько авто-грейдимых (с баллом) ответов в срезе.</param>
/// <param name="Total">Сколько всего item'ов в срезе (включая неотвеченные/OPEN_TEXT).</param>
public sealed record SessionBreakdownDto(
    string Key,
    int Correct,
    int Graded,
    int Total);

/// <summary>
///     Разбивка результатов сессии: общий correct/total + срезы по сложности
///     (JUNIOR/MIDDLE/SENIOR) и по теме. Зеркалит курсовую статистику тестов платформы.
/// </summary>
public sealed record SessionStatsDto(
    Guid Id,
    string Mode,
    string Status,
    int? ScorePercent,
    int TotalItems,
    int AnsweredItems,
    int CorrectItems,
    int GradedItems,
    IReadOnlyList<SessionBreakdownDto> ByDifficulty,
    IReadOnlyList<SessionBreakdownDto> ByTopic);
