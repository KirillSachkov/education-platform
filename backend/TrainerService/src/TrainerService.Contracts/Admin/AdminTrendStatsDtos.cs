using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/trends?days=N (#681 T6) ===

/// <summary>
///     Owner-кривая динамики тренажёра по дням (#681 T6): плотный дневной ряд платформенных KPI за окно
///     <c>days</c> из снапшотов (<c>daily_stat_snapshots</c>). Отдельно от cost/usage-дашборда
///     (<see cref="AdminStatsDto"/>, T2) и контент-аналитики (<see cref="AdminTopicBankStatsDto"/>, T5).
///     Точка на КАЖДЫЙ день окна (пропуски занулены — zero-fill, как <c>BuildDenseDaily</c> в T2), чтобы
///     recharts не рисовал разрывы. Read-only поверх снапшотов, без EF-загрузки сущностей.
/// </summary>
/// <param name="Days">Окно в днях (clamp 1..365, default 30). Ряд содержит <c>Days + 1</c> точек (cutoff-день .. сегодня включительно).</param>
/// <param name="Points">Плотный дневной ряд, oldest-first.</param>
public sealed record AdminTrendStatsDto(
    int Days,
    IReadOnlyList<AdminTrendPointDto> Points);

/// <summary>
///     Один день owner-кривой: платформенные KPI за день (zero-filled, если в этот день не было снимка/
///     активности). Стоимость — в микрорублях (источник правды) + ₽ (для показа).
/// </summary>
/// <param name="Date">UTC-дата дня.</param>
/// <param name="SessionsStarted">Сессий стартовано в этот день.</param>
/// <param name="ActiveUsers">Distinct пользователей, стартовавших хоть одну сессию в этот день.</param>
/// <param name="CompletedSessions">Сессий завершено в этот день.</param>
/// <param name="CostMicroRub">Суммарная стоимость AI-вызовов за день в микрорублях (₽ × 1 000 000).</param>
/// <param name="CostRub">То же в рублях (<see cref="CostMicroRub"/> / 1 000 000).</param>
/// <param name="AvgAccuracyPct">Средняя точность (балл 0..100) по оценённым ответам дня (0, если оценённых нет).</param>
public sealed record AdminTrendPointDto(
    DateOnly Date,
    int SessionsStarted,
    int ActiveUsers,
    int CompletedSessions,
    long CostMicroRub,
    decimal CostRub,
    double AvgAccuracyPct);
