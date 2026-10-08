namespace TrainerService.Contracts.Progress;

/// <summary>
///     Mastery + study-аналитика вызывающего по одной теме. <see cref="MasteryPercent"/>/
///     <see cref="AnswersCount"/> — из <c>TopicMastery</c> (питается тестами/learn-by-test).
///     <see cref="StudiedCount"/> (изучено = статус SEEN/KNOWN/REVIEW/WRONG, т.е. любая строка
///     study-state) и <see cref="MistakesCount"/> (WRONG/REVIEW) — из <c>QuestionStudyState</c>
///     (#568 Ф2; питается карточками + learn-by-test). <see cref="CoveragePercent"/> — «освоение»
///     (#664): доля вопросов темы, решённых ВЕРНО, из всех вопросов её банков (не EWMA).
///     <see cref="LastPractisedAt"/> — последняя активность темы (mastery либо study-state, что свежее).
/// </summary>
public sealed record TopicMasteryDto(
    Guid TopicId,
    int MasteryPercent,
    int CoveragePercent,
    bool IsWeak,
    int AnswersCount,
    int StudiedCount,
    int MistakesCount,
    DateTime LastPractisedAt);

/// <summary>Краткая запись недавней сессии в прогресс-сводке.</summary>
public sealed record RecentSessionDto(
    Guid Id,
    string Mode,
    string Status,
    IReadOnlyList<Guid> TopicIds,
    int? ScorePercent,
    DateTime StartedAt,
    DateTime? CompletedAt);

/// <summary>Прогресс-сводка вызывающего: mastery по всем темам + недавние сессии.</summary>
public sealed record TrainerProgressDto(
    IReadOnlyList<TopicMasteryDto> Mastery,
    IReadOnlyList<RecentSessionDto> RecentSessions);
