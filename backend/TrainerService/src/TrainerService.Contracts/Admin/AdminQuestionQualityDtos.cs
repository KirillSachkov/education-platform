using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/question-quality?days=N ===

/// <summary>
///     Admin question-quality снимок тренажёра за окно <c>days</c> (#681 T4): по-вопросные метрики
///     качества банка поверх снапшотов ответов (<c>trainer.training_session_items</c>) джойнятся к
///     собственным вопросам тренажёра (<c>trainer_questions → topic_banks → topics</c>). Read-only.
///     Скоуп окна — по <c>training_sessions.started_at &gt;= now - days</c> (включает и отвеченные, и
///     пропущенные item'ы — нужно для skip-rate). Помогает автору находить «битые» вопросы (аномально
///     низкий %-верных при достаточной выборке) и оценивать дискриминативность.
/// </summary>
/// <param name="Days">Эффективное окно в днях (clamp 1..365, default 30).</param>
/// <param name="OutlierCapSeconds">
///     Верхняя отсечка для time-per-question приближения (диффы выше — отбрасываются как «отошёл от
///     экрана»). Документирует ограничение метрики времени (см. <see cref="AdminQuestionQualityItemDto.AvgSecondsPerQuestion"/>).
/// </param>
/// <param name="Questions">Вопросы, по которым в окне есть хотя бы один снапшот-item.</param>
public sealed record AdminQuestionQualityStatsDto(
    int Days,
    int OutlierCapSeconds,
    IReadOnlyList<AdminQuestionQualityItemDto> Questions);

/// <summary>
///     Метрики качества одного вопроса. Поля для сортировки/флага «переписать» на фронте: сортировать
///     по <see cref="CorrectRate"/>, флажить аномально низкий <see cref="CorrectRate"/> при достаточном
///     <see cref="Attempts"/> (низкая выборка отсекается порогом на фронте).
/// </summary>
/// <param name="QuestionId">Идентичность вопроса собственного банка тренажёра.</param>
/// <param name="Stem">Текст вопроса (для показа в таблице качества).</param>
/// <param name="QuestionType">SINGLE_CHOICE / MULTI_CHOICE / EXACT_TEXT / OPEN_TEXT.</param>
/// <param name="Difficulty">JUNIOR / MIDDLE / SENIOR или null.</param>
/// <param name="Section">Раздел вопроса (опц.).</param>
/// <param name="TopicId">Тема-владелец (через банк).</param>
/// <param name="TopicTitle">Заголовок темы (для группировки в UI).</param>
/// <param name="BankId">Банк-владелец вопроса.</param>
/// <param name="Attempts">Сколько раз на вопрос ответили (item'ы с <c>answered_at</c> NOT NULL) в окне.</param>
/// <param name="CorrectCount">Из них с вердиктом CORRECT.</param>
/// <param name="CorrectRate">
///     Доля верных = <see cref="CorrectCount"/> / <see cref="Attempts"/> (0..1), null при <c>Attempts=0</c>.
///     Считается по ВЕРДИКТУ (CORRECT), а не по баллу — у открытого ответа CORRECT может быть с не-100 баллом (#678).
/// </param>
/// <param name="Discrimination">
///     Point-biserial-подобный прокси дискриминативности: (%-верных у сильной группы − %-верных у слабой).
///     Группы = NTILE(2) пользователей по их ОБЩЕЙ точности в окне. Диапазон −1..+1; положительное — вопрос
///     отделяет сильных от слабых, около нуля / отрицательное — кандидат на пересмотр. null если нельзя
///     посчитать (нет ответов в одной из групп, напр. &lt; 2 различимых пользователей). Метод — в комментарии запроса.
/// </param>
/// <param name="TopGroupCorrectRate">%-верных у сильной группы (NTILE верхний), null если нет её ответов.</param>
/// <param name="BottomGroupCorrectRate">%-верных у слабой группы (NTILE нижний), null если нет её ответов.</param>
/// <param name="CompletedItems">Сколько item'ов этого вопроса в ЗАВЕРШЁННЫХ сессиях (знаменатель skip-rate).</param>
/// <param name="SkippedItems">Из них пропущенных (<c>answered_at</c> NULL в COMPLETED-сессии).</param>
/// <param name="SkipRate">
///     <see cref="SkippedItems"/> / <see cref="CompletedItems"/> (0..1), null при <c>CompletedItems=0</c>.
///     Скоуп — только завершённые сессии: незаданные ответы в IN_PROGRESS-сессии не считаются пропуском.
/// </param>
/// <param name="AvgSecondsPerQuestion">
///     Приближение времени на вопрос: средняя разница между соседними <c>answered_at</c> внутри сессии
///     (нет served-at/duration-колонки — документированное ограничение). Дифф приписывается ВТОРОМУ
///     (текущему) ответу. Отброшены неположительные и превышающие <see cref="AdminQuestionQualityStatsDto.OutlierCapSeconds"/>.
///     null если нет измеримых соседних диффов (напр. вопрос всегда первый в сессии).
/// </param>
/// <param name="TimeSampleCount">Сколько валидных диффов времени попало в среднее.</param>
/// <param name="OpenText">Для OPEN_TEXT — разбивка по вердиктам + распределение AI-балла; иначе null.</param>
public sealed record AdminQuestionQualityItemDto(
    Guid QuestionId,
    string Stem,
    string QuestionType,
    string? Difficulty,
    string? Section,
    Guid TopicId,
    string TopicTitle,
    Guid BankId,
    long Attempts,
    long CorrectCount,
    double? CorrectRate,
    double? Discrimination,
    double? TopGroupCorrectRate,
    double? BottomGroupCorrectRate,
    long CompletedItems,
    long SkippedItems,
    double? SkipRate,
    double? AvgSecondsPerQuestion,
    long TimeSampleCount,
    AdminOpenTextQualityDto? OpenText);

/// <summary>
///     OPEN_TEXT-специфика: разбивка вердиктов AI-грейда (CORRECT/PARTIAL/INCORRECT) + распределение
///     балла по бэндам, зеркалящим грейдер (#678): 0–39 INCORRECT / 40–79 PARTIAL / 80–100 CORRECT.
///     Бэкеты считаются по самому баллу (независимо от вердикта) — видно дрейф калибровки.
/// </summary>
/// <param name="Correct">Открытых ответов с вердиктом CORRECT.</param>
/// <param name="Partial">… с вердиктом PARTIAL.</param>
/// <param name="Incorrect">… с вердиктом INCORRECT.</param>
/// <param name="ScoreBucketLow">Балл 0–39.</param>
/// <param name="ScoreBucketMid">Балл 40–79.</param>
/// <param name="ScoreBucketHigh">Балл 80–100.</param>
public sealed record AdminOpenTextQualityDto(
    long Correct,
    long Partial,
    long Incorrect,
    long ScoreBucketLow,
    long ScoreBucketMid,
    long ScoreBucketHigh);
