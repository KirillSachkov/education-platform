namespace TrainerService.Contracts.Topics;

/// <summary>Запрос на создание темы тренажёра (admin/seed). Тема относится к треку.</summary>
public sealed record CreateTopicRequest(
    Guid TrackId,
    string? Slug,
    string? Title,
    string? Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId);

/// <summary>Запрос на обновление деталей темы (admin/seed). TrackId переназначает трек.</summary>
public sealed record UpdateTopicRequest(
    Guid TrackId,
    string? Title,
    string? Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId);

/// <summary>Запрос на добавление банка вопросов к теме (admin/seed). Создаёт пустой банк — вопросы добавляются отдельно через question CRUD (#623).</summary>
public sealed record AddTopicBankRequest(
    string? Tier,
    string? Difficulty);

/// <summary>Идентификатор созданного ресурса.</summary>
public sealed record TopicIdResponse(Guid TopicId);

/// <summary>Идентификатор созданного банка.</summary>
public sealed record TopicBankIdResponse(Guid BankId);

/// <summary>Банк вопросов темы (admin-проекция).</summary>
public sealed record TopicBankDto(
    Guid Id,
    string Tier,
    string? Difficulty);

/// <summary>Запрос на обновление банка вопросов (admin). TopicId immutable.</summary>
public sealed record UpdateTopicBankRequest(
    string? Tier,
    string? Difficulty,
    string? Purpose);

/// <summary>Запрос на смену tier банка вопросов (admin).</summary>
public sealed record SetTopicBankTierRequest(string? Tier);

/// <summary>Полный банк вопросов темы (admin builder-проекция) — включает purpose + sortKey + число вопросов (#623).</summary>
public sealed record TopicBankAdminDto(
    Guid Id,
    Guid TopicId,
    string Tier,
    string? Difficulty,
    string Purpose,
    string SortKey,
    int QuestionCount,
    DateTime CreatedAt);

/// <summary>
///     Тема в admin-проекции (включая DRAFT) — полные метаданные без персонального mastery/фримиума.
///     Питает список тем и карточку темы в редакторе автора.
/// </summary>
public sealed record TopicAdminDto(
    Guid Id,
    Guid TrackId,
    string Slug,
    string Title,
    string Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId,
    string SortKey,
    bool IsPublished,
    int BankCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
///     Тема в студенческом списке/прогресс-карте: метаданные темы + персональный mastery
///     вызывающего + фримиум-флаги (есть ли бесплатный банк / заблокирована ли тема целиком).
///     <para>
///         <see cref="CoveragePercent"/> = «освоение» (#664): доля вопросов темы, решённых ВЕРНО,
///         из всех вопросов её банков. В отличие от EWMA-<see cref="MasteryPercent"/> (взлетает до
///         100% с 1-2 верных ответов), покрытие отражает реальный прогресс по теме. Фронт показывает
///         именно его в «Освоение», а «Хорошо изучена» — только при высоком покрытии.
///     </para>
///     <para>
///         <see cref="IsLocked"/> = у темы есть банки, но все PAID, а у вызывающего нет PRO-подписки
///         (<c>cap:TRAINER_PRO</c>) и он не admin. <see cref="LockReason"/> — машинно-читаемая причина
///         для фронта (paywall-копирайт): <c>"pro_required"</c> когда заблокирована, иначе <c>null</c>.
///     </para>
/// </summary>
public sealed record TopicListItemDto(
    Guid Id,
    Guid TrackId,
    string Slug,
    string Title,
    string Area,
    string? Description,
    string? Direction,
    Guid? RecommendedCourseId,
    Guid? FallbackCourseId,
    int MasteryPercent,
    int CoveragePercent,
    bool IsWeak,
    int AnswersCount,
    bool HasFreeBank,
    bool IsLocked,
    string? LockReason);
