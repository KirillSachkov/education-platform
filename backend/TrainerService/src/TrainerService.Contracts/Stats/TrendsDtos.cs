using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Stats;

// === GET /trainer/stats/trends (#681 T6) ===

/// <summary>
///     Динамика mastery вызывающего по одной теме «vs месяц назад» (#681 T6): текущий снимок против
///     снимка ~<see cref="TrainerTrendsDto.ComparisonDays"/> дней назад. <see cref="MasteryThen"/> и
///     <see cref="Delta"/> = null, если у темы ещё нет исторического снимка (ранний пользователь —
///     истории нет). Положительная <see cref="Delta"/> = тема подтянулась за месяц.
/// </summary>
/// <param name="TopicId">Тема.</param>
/// <param name="TopicTitle">Заголовок темы (fallback «Тема», если строка темы удалена).</param>
/// <param name="MasteryNow">Mastery (0..100) на сегодняшний снимок.</param>
/// <param name="MasteryThen">Mastery (0..100) на снимок ~месяц назад; null, если снимка нет.</param>
/// <param name="Delta">MasteryNow − MasteryThen; null, если нет исторического снимка.</param>
public sealed record TopicMasteryTrendDto(
    Guid TopicId,
    string TopicTitle,
    int MasteryNow,
    int? MasteryThen,
    int? Delta);

/// <summary>
///     Тренд mastery вызывающего «vs месяц назад» (#681 T6): per-topic сравнение текущего снимка
///     mastery со снимком <see cref="ComparisonDays"/>-дневной давности + общая средняя дельта.
///     Питается из снапшотов (<c>topic_mastery_snapshots</c>), а не из живого mastery — так получается
///     честная история. Темы — все, у кого есть СЕГОДНЯШНИЙ снимок (отсортированы: с историей и большим
///     ростом вверх, новые — вниз). Overall-поля считаются ТОЛЬКО по «сравнимым» темам (есть оба снимка),
///     поэтому <see cref="OverallMasteryNow"/> − <see cref="OverallMasteryThen"/> = <see cref="OverallDelta"/>.
///     Все три overall-поля = null, если ни у одной темы нет исторического снимка. Own-data (scoped по UserId).
/// </summary>
/// <param name="ComparisonDays">На сколько дней назад берётся «было» (фиксировано 30).</param>
/// <param name="Topics">Per-topic срез (все темы с сегодняшним снимком), отсортированный.</param>
/// <param name="OverallMasteryNow">Средний текущий mastery по сравнимым темам; null, если сравнимых нет.</param>
/// <param name="OverallMasteryThen">Средний mastery месяц назад по сравнимым темам; null, если сравнимых нет.</param>
/// <param name="OverallDelta">Общая средняя дельта (now − then) по сравнимым темам; null, если сравнимых нет.</param>
public sealed record TrainerTrendsDto(
    int ComparisonDays,
    IReadOnlyList<TopicMasteryTrendDto> Topics,
    int? OverallMasteryNow,
    int? OverallMasteryThen,
    int? OverallDelta);
