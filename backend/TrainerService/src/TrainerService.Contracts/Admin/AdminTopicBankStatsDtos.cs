using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/topics?days=N (#681 T5) ===

/// <summary>
///     Контент-аналитика тренажёра для автора/админа (#681 T5): per-topic / per-bank срезы + калибровка
///     сложности — отдельно от cost/usage-дашборда (<see cref="AdminStatsDto"/>, который владеет T2). Окно
///     <c>days</c> относится к АКТИВНОСТИ (ответы/сессии за <c>answered_at &gt;= now-days</c>) и калибровке;
///     агрегатный mastery темы — это ТЕКУЩИЙ снимок (derived — взвешенное среднее последних баллов по
///     уникальным вопросам; не оконный). Dapper raw-SQL
///     агрегаты без EF-загрузки сущностей — зеркалит <see cref="AdminStatsDto"/> / AccessService plan-stats.
/// </summary>
/// <param name="Days">Окно активности/калибровки в днях (clamp 1..365, default 30).</param>
/// <param name="Topics">Per-topic срез: mastery (снимок) + %-верных и объём за окно. Сложные темы — вверху.</param>
/// <param name="Banks">Per-bank срез: покрытие (отвечают ли вопросы банка) + разбивка по типу и сложности.</param>
/// <param name="Calibration">Калибровка сложности: заявленный уровень vs фактический %-верных + выбросы.</param>
public sealed record AdminTopicBankStatsDto(
    int Days,
    IReadOnlyList<AdminTopicStatDto> Topics,
    IReadOnlyList<AdminBankStatDto> Banks,
    AdminDifficultyCalibrationDto Calibration);

// --- Per-topic ---

/// <summary>
///     Срез по одной теме. <see cref="AvgMasteryPercent"/>/<see cref="MasteryUsers"/> — текущий снимок
///     mastery (по всем строкам <c>topic_masteries</c>, не оконный); остальное — активность за окно.
/// </summary>
/// <param name="TopicId">Тема.</param>
/// <param name="TopicTitle">Заголовок темы (fallback «Тема», если строка темы удалена).</param>
/// <param name="AvgMasteryPercent">Средний mastery по теме среди всех практиковавших (0 если нет данных).</param>
/// <param name="MasteryUsers">Сколько пользователей имеют mastery-строку по теме.</param>
/// <param name="AvgCorrectPercent">Средний %-верных по оценённым ответам темы за окно (0 если нет ответов).</param>
/// <param name="AnswersCount">Сколько оценённых ответов по теме за окно.</param>
/// <param name="SessionsCount">Сколько различных сессий затронули тему (по оценённым ответам) за окно.</param>
public sealed record AdminTopicStatDto(
    Guid TopicId,
    string TopicTitle,
    int AvgMasteryPercent,
    long MasteryUsers,
    double AvgCorrectPercent,
    long AnswersCount,
    long SessionsCount);

// --- Per-bank ---

/// <summary>
///     Срез по одному банку вопросов. <see cref="CoveragePercent"/> = доля вопросов банка, по которым реально
///     отвечали за окно (<see cref="AnsweredQuestions"/> / <see cref="TotalQuestions"/> × 100) — низкое
///     покрытие = «банк простаивает». Плюс разбивка состава банка по типу и сложности вопросов.
/// </summary>
/// <param name="BankId">Банк.</param>
/// <param name="TopicId">Тема-владелец банка.</param>
/// <param name="TopicTitle">Заголовок темы (fallback «Тема»).</param>
/// <param name="Tier">Tier банка (FREE/PAID — dormant #674, для справки).</param>
/// <param name="Difficulty">Заявленная сложность банка (JUNIOR/MIDDLE/SENIOR), может быть null.</param>
/// <param name="Purpose">Назначение банка (STUDY/MOCK).</param>
/// <param name="TotalQuestions">Сколько всего вопросов в банке.</param>
/// <param name="AnsweredQuestions">Сколько различных вопросов банка получили хотя бы один оценённый ответ за окно.</param>
/// <param name="CoveragePercent">Покрытие = AnsweredQuestions / TotalQuestions × 100 (0 при пустом банке).</param>
/// <param name="AnswersCount">Всего оценённых ответов по вопросам банка за окно.</param>
/// <param name="ByType">Разбивка вопросов банка по типу (SINGLE_CHOICE/MULTI_CHOICE/EXACT_TEXT/OPEN_TEXT).</param>
/// <param name="ByDifficulty">Разбивка вопросов банка по сложности (JUNIOR/MIDDLE/SENIOR/UNSPECIFIED).</param>
public sealed record AdminBankStatDto(
    Guid BankId,
    Guid TopicId,
    string TopicTitle,
    string Tier,
    string? Difficulty,
    string Purpose,
    long TotalQuestions,
    long AnsweredQuestions,
    double CoveragePercent,
    long AnswersCount,
    IReadOnlyList<AdminQuestionTypeCountDto> ByType,
    IReadOnlyList<AdminQuestionDifficultyCountDto> ByDifficulty);

/// <summary>Сколько вопросов банка одного типа.</summary>
public sealed record AdminQuestionTypeCountDto(
    string Type,
    long Count);

/// <summary>Сколько вопросов банка одной сложности (<c>UNSPECIFIED</c> — без заявленной сложности).</summary>
public sealed record AdminQuestionDifficultyCountDto(
    string Difficulty,
    long Count);

// --- Difficulty calibration ---

/// <summary>
///     Калибровка сложности: заявленный уровень вопроса (<c>QuestionDifficulty</c>) против ФАКТИЧЕСКОГО
///     %-верных, агрегированного по уровню (<see cref="Levels"/>), + выбросы отдельных вопросов
///     (<see cref="Miscalibrated"/>). Помогает поймать неверно проставленную сложность: JUNIOR с низким
///     %-верных (на деле трудный) или SENIOR с высоким (на деле лёгкий).
/// </summary>
/// <param name="Levels">Фактический %-верных + объём по каждому ЗАЯВЛЕННОМУ уровню сложности.</param>
/// <param name="Miscalibrated">Вопросы, чей %-верных сильнее всего отклоняется от среднего своего уровня.</param>
public sealed record AdminDifficultyCalibrationDto(
    IReadOnlyList<AdminCalibrationLevelDto> Levels,
    IReadOnlyList<AdminMiscalibratedQuestionDto> Miscalibrated);

/// <summary>
///     Фактический результат по одному заявленному уровню сложности за окно. JUNIOR/MIDDLE/SENIOR
///     всегда присутствуют (нулями при отсутствии данных); <c>UNSPECIFIED</c> добавляется только если
///     есть оценённые ответы на вопросы без заявленной сложности.
/// </summary>
/// <param name="Difficulty">Заявленный уровень (JUNIOR/MIDDLE/SENIOR/UNSPECIFIED).</param>
/// <param name="ActualCorrectPercent">Средний %-верных по оценённым ответам этого уровня за окно.</param>
/// <param name="AnswersCount">Сколько оценённых ответов на этом уровне за окно.</param>
/// <param name="QuestionsAnswered">Сколько различных вопросов этого уровня получили ответ за окно.</param>
public sealed record AdminCalibrationLevelDto(
    string Difficulty,
    double ActualCorrectPercent,
    long AnswersCount,
    long QuestionsAnswered);

/// <summary>
///     Один вопрос-выброс: его фактический %-верных и отклонение <see cref="DeltaVsLevel"/> от среднего
///     %-верных своего заявленного уровня. Сильно отрицательная дельта у JUNIOR = вопрос труднее метки;
///     сильно положительная у SENIOR = легче метки. Отсортированы по |дельте| убыв., список ограничен.
/// </summary>
/// <param name="QuestionId">Вопрос.</param>
/// <param name="BankId">Банк-владелец.</param>
/// <param name="TopicId">Тема-владелец.</param>
/// <param name="TopicTitle">Заголовок темы (fallback «Тема»).</param>
/// <param name="Difficulty">Заявленная сложность вопроса.</param>
/// <param name="Stem">Текст вопроса (admin-only, чтобы автор нашёл вопрос).</param>
/// <param name="ActualCorrectPercent">Средний %-верных по оценённым ответам этого вопроса за окно.</param>
/// <param name="DeltaVsLevel">ActualCorrectPercent минус средний %-верных заявленного уровня.</param>
/// <param name="AnswersCount">Сколько оценённых ответов на этот вопрос за окно (для оценки выборки).</param>
public sealed record AdminMiscalibratedQuestionDto(
    Guid QuestionId,
    Guid BankId,
    Guid TopicId,
    string TopicTitle,
    string Difficulty,
    string Stem,
    double ActualCorrectPercent,
    double DeltaVsLevel,
    long AnswersCount);
