using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Stats;

// === GET /trainer/stats/activity ===

/// <summary>
///     Активность одного календарного дня (UTC) вызывающего: сколько сессий начато, сколько
///     авто-грейдимых ответов дано и сколько из них верных. <see cref="AccuracyPercent"/> =
///     round(correct/answered*100), 0 при answered=0.
/// </summary>
public sealed record TrainerActivityDayDto(
    DateOnly Date,
    int Sessions,
    int Answered,
    int Correct,
    int AccuracyPercent);

/// <summary>
///     Временная ось активности тренажёра (#568): по одному элементу на каждый день диапазона,
///     где была хоть какая-то активность (фронт сам дорисовывает пропуски). <see cref="CurrentStreak"/> —
///     количество дней подряд с ≥1 начатой сессией, считая до сегодня (UTC); <see cref="LongestStreak"/> —
///     самая длинная серия за всё время (не ограничена окном <c>days</c>).
/// </summary>
public sealed record TrainerActivityDto(
    IReadOnlyList<TrainerActivityDayDto> Days,
    int CurrentStreak,
    int LongestStreak);

// === GET /trainer/stats/summary ===

/// <summary>Срез «покрытия материала» — сколько вопросов в каждом study-статусе (SEEN/KNOWN/REVIEW/WRONG).</summary>
public sealed record StudyStatusCountDto(
    string Status,
    int Count);

/// <summary>
///     Точность по уровню сложности (JUNIOR/MIDDLE/SENIOR): сколько авто-грейдимых ответов дано
///     и сколько верных за всё время. Уровень берётся со снапшота item'а сессии
///     (<c>TrainingSessionItem.Difficulty</c>). Все три бакета возвращаются всегда (нулями при отсутствии данных).
/// </summary>
public sealed record DifficultyAccuracyDto(
    string Difficulty,
    int Answered,
    int Correct,
    int AccuracyPercent);

/// <summary>Сколько вопросов «на повтор» приходится на один будущий день (по <c>next_due_at::date</c>).</summary>
public sealed record SrsUpcomingDayDto(
    DateOnly Date,
    int Due);

/// <summary>
///     SRS-прогноз вызывающего: <see cref="DueToday"/> — вопросов с <c>next_due_at &lt;= now</c>;
///     <see cref="Upcoming"/> — разбивка на следующие 7 дней (только дни с долгом);
///     <see cref="RetentionPercent"/> = sum(TimesKnown)/sum(TimesSeen)*100 (0 при отсутствии повторов).
/// </summary>
public sealed record SrsForecastDto(
    int DueToday,
    IReadOnlyList<SrsUpcomingDayDto> Upcoming,
    int RetentionPercent);

/// <summary>
///     All-time сводка тренажёра вызывающего (#568): объём отвеченного + точность, разбивка study-статусов
///     («покрытие материала»), число изученных вопросов, точность по сложности и SRS-прогноз.
/// </summary>
/// <param name="TotalAnswered">Всего авто-грейдимых ответов (с баллом) за всё время по всем сессиям.</param>
/// <param name="AllTimeAccuracyPercent">correct/answered*100 за всё время (0 при answered=0).</param>
/// <param name="StudyStatusBreakdown">Сколько вопросов в каждом study-статусе (для доната покрытия).</param>
/// <param name="StudiedQuestions">Число различных вопросов, по которым есть study-state (= изучено).</param>
/// <param name="DifficultyAccuracy">Точность по JUNIOR/MIDDLE/SENIOR (все три бакета всегда).</param>
/// <param name="Srs">SRS-прогноз: на повтор сегодня + 7-дневный график + retention.</param>
public sealed record TrainerStatsSummaryDto(
    int TotalAnswered,
    int AllTimeAccuracyPercent,
    IReadOnlyList<StudyStatusCountDto> StudyStatusBreakdown,
    int StudiedQuestions,
    IReadOnlyList<DifficultyAccuracyDto> DifficultyAccuracy,
    SrsForecastDto Srs);

// === GET /trainer/stats/mock-trend ===

/// <summary>Одна завершённая мок-сессия в тренде: id, когда завершена, итоговый балл (0..100).</summary>
public sealed record MockAttemptDto(
    Guid SessionId,
    DateTime CompletedAt,
    int ScorePercent);

/// <summary>
///     Тренд мок-собесов вызывающего (#568): динамика баллов завершённых MOCK-сессий (по возрастанию
///     даты) + агрегированные слабые/сильные темы из AI-разбора (<c>AiWeakTopicsJson</c>/<c>AiStrengthsJson</c>),
///     дедуп + cap. Пустые массивы, если у юзера нет завершённых моков / AI-фидбэка.
/// </summary>
public sealed record TrainerMockTrendDto(
    IReadOnlyList<MockAttemptDto> Attempts,
    IReadOnlyList<string> WeakTopics,
    IReadOnlyList<string> StrongTopics);

// === GET /trainer/stats/strengths ===

/// <summary>
///     Одна тема в рейтинге сильных/слабых сторон (#614 H): название + измеренный mastery (0..100) +
///     число оценённых ответов, на которых он стоит. Питается из <c>TopicMastery</c> (derived —
///     взвешенное среднее последних баллов по уникальным вопросам), не из AI.
/// </summary>
public sealed record StrengthTopicDto(
    Guid TopicId,
    string Title,
    int MasteryPercent,
    int Attempts);

/// <summary>
///     Объём данных, на котором стоит оценка сильных/слабых сторон (#614 H, контекст «sample size»):
///     сколько тем оценено (прошли порог попыток), сколько всего ответов их питает, сколько сессий
///     пройдено и сколько из них — мок-собесы. UI рендерит строкой «оценка по N ответам в M сессиях».
/// </summary>
public sealed record StrengthSampleSizeDto(
    int AssessedTopics,
    int TotalAnswers,
    int SessionsCount,
    int MockCount);

/// <summary>
///     Сильные и слабые стороны вызывающего по ИЗМЕРЕННОМУ mastery (#614 H) поверх всей активности
///     тренажёра (drill/learn/test/mock), а не из одного AI-разбора. <see cref="StrongTopics"/> —
///     темы с mastery &gt;= 75, <see cref="WeakTopics"/> — &lt; 60; обе только при &gt;= 3 оценённых
///     ответах (тема с малым числом попыток в колонки не попадает). <see cref="SampleSize"/> — контекст
///     объёма. <see cref="MockHintTopics"/> — де-шумленный вторичный сигнал из AI-разбора мок-собесов
///     (тема засчитывается, только если упомянута в &gt;= 2 завершённых моках) — подсказка, не основа колонок.
/// </summary>
public sealed record TrainerStrengthsDto(
    IReadOnlyList<StrengthTopicDto> StrongTopics,
    IReadOnlyList<StrengthTopicDto> WeakTopics,
    StrengthSampleSizeDto SampleSize,
    IReadOnlyList<string> MockHintTopics);
