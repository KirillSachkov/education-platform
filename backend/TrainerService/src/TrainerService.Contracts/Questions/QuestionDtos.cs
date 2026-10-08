namespace TrainerService.Contracts.Questions;

/// <summary>
///     Вопрос в списке охвата (тема) — студенческая проекция для режима «Изучение» (#568 Ф2).
///     <b>Без ключа грейдинга / правильных ответов</b>: только метаданные + персональный статус
///     изучения + флаг закладки. Статус берётся из <c>QuestionStudyState</c>; отсутствие строки =
///     <c>NEW</c> (значение <see cref="Status"/> = <c>"NEW"</c>). Питает «Список вопросов» (вход в
///     Карточки) и каталог тем. Для free-тем список публичен (анонимный просмотр → SEO).
/// </summary>
/// <param name="Status">SEEN | KNOWN | REVIEW | WRONG из <c>QuestionStudyState</c>, либо NEW если строки ещё нет.</param>
/// <param name="IsLocked">
///     Монетизация по типу вопроса (#623): развёрнутый (OPEN_TEXT, голос → AI-разбор) вопрос
///     заблокирован для не-PRO ИЛИ тема целиком за PRO. Закрытые тесты (choice/exact) у free открыты.
///     Стем перечисляется даже для locked (как каталог) — ответы не утекают. LockReason="pro_required".
/// </param>
public sealed record QuestionListItemDto(
    Guid QuestionId,
    // Nullable (#674): nulled out server-side when the item is locked for a non-PRO caller (redaction).
    string? Stem,
    string Type,
    string? Difficulty,
    string? Section,
    string Status,
    bool IsBookmarked,
    bool IsLocked = false,
    string? LockReason = null);

/// <summary>
///     Список вопросов охвата + контекст темы (заблокирована ли она целиком фримиум-гейтом).
///     Для PRO-темы без доступа метаданные всё равно перечисляются (<see cref="IsLocked"/>=true,
///     <see cref="Items"/> со стемами без ответов) — как каталог курсов.
///     <para>
///         <see cref="IsLocked"/> = у темы только PAID-банки и у вызывающего нет PRO (и он не admin).
///         <see cref="LockReason"/> — машинно-читаемая причина для paywall-копирайта фронта:
///         <c>"pro_required"</c> когда заблокирована, иначе <c>null</c>.
///     </para>
/// </summary>
public sealed record QuestionListDto(
    Guid TopicId,
    bool IsLocked,
    string? LockReason,
    IReadOnlyList<QuestionListItemDto> Items);

/// <summary>
///     Вопрос «на повтор сегодня» в кросс-тематической SRS-очереди вызывающего: id вопроса,
///     тема-источник, стем. Только собственные данные.
/// </summary>
public sealed record SrsDueItemDto(
    Guid QuestionId,
    Guid TopicId,
    // Nullable (#674): nulled out when the question is locked for a non-PRO caller (redaction).
    string? Stem,
    string? Difficulty,
    string Status,
    DateTime? NextDueAt,
    bool IsLocked = false,
    string? LockReason = null);

/// <summary>
///     Вопрос в кросс-тематическом списке «Мои ошибки» (статус WRONG/REVIEW): id, тема, стем +
///     счётчики. Только собственные данные.
/// </summary>
public sealed record MistakeItemDto(
    Guid QuestionId,
    Guid TopicId,
    // Nullable (#674): nulled out when the question is locked for a non-PRO caller (redaction).
    string? Stem,
    string? Difficulty,
    string Status,
    int TimesWrong,
    DateTime LastSeenAt,
    DateTime? NextDueAt,
    bool IsLocked = false,
    string? LockReason = null);

// --- Admin question CRUD (#623) — собственный банк вопросов тренажёра. ---

/// <summary>Вариант ответа в запросе создания/обновления вопроса (admin). Только для choice-типов.</summary>
public sealed record QuestionOptionInputDto(string Text, bool IsCorrect);

/// <summary>
///     Запрос на создание/обновление вопроса банка (admin). <see cref="Type"/> —
///     SINGLE_CHOICE | MULTI_CHOICE | EXACT_TEXT | OPEN_TEXT. Для choice-типов нужны
///     <see cref="Options"/> (≥2, с признаком правильности); для EXACT_TEXT — <see cref="ReferenceAnswer"/>.
/// </summary>
public sealed record QuestionInputDto(
    string? Stem,
    string Type,
    string? ReferenceAnswer,
    string? Explanation,
    string? Difficulty,
    string? Section,
    IReadOnlyList<QuestionOptionInputDto>? Options);

/// <summary>Id созданного/обновлённого вопроса.</summary>
public sealed record QuestionIdResponse(Guid Id);

/// <summary>
///     Вариант ответа в admin-проекции вопроса (редактор): включает <see cref="IsCorrect"/>.
///     Никогда не отдаётся студентам — только в admin question CRUD.
/// </summary>
public sealed record QuestionOptionAdminDto(Guid Id, string Text, bool IsCorrect, int SortIndex);

/// <summary>
///     Полный вопрос банка (admin builder-проекция) — для редактора: включает варианты с признаком
///     правильности + эталон + разбор + sortKey. Только admin (role ADMIN); студентам ответы не утекают.
/// </summary>
public sealed record QuestionAdminDto(
    Guid Id,
    Guid BankId,
    string Stem,
    string Type,
    string? ReferenceAnswer,
    string? Explanation,
    string? Difficulty,
    string? Section,
    string SortKey,
    IReadOnlyList<QuestionOptionAdminDto> Options,
    DateTime CreatedAt,
    DateTime UpdatedAt);
