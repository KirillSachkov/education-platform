using System;
using System.Collections.Generic;

namespace TrainerService.Contracts.Admin;

// === GET /trainer/admin/stats/traffic?days=N (#681 T3) ===

/// <summary>
///     Admin-снимок ТРАФИКА тренажёра за окно <c>days</c> (clamp 1..365, default 30): активные
///     пользователи (DAU/WAU/MAU), retention (D1/D7/D30 по first-touch когортам), новые vs
///     вернувшиеся по дням и число сессий по дням в разрезе режима. Источник — <c>trainer.training_sessions</c>
///     (Dapper-агрегаты, без EF-загрузки), зеркалит <see cref="AdminStatsDto"/>.
/// </summary>
public sealed record AdminTrafficStatsDto(
    int Days,
    AdminActiveUsersDto ActiveUsers,
    AdminRetentionDto Retention,
    IReadOnlyList<AdminNewReturningDayDto> NewVsReturning,
    IReadOnlyList<AdminSessionsByModeDayDto> SessionsByMode);

/// <summary>
///     Активные пользователи в фиксированных скользящих окнах (НЕ зависят от <c>days</c>): distinct
///     <c>user_id</c> с ≥1 начатой сессией за последние 1 / 7 / 30 дней (по <c>started_at</c>).
/// </summary>
public sealed record AdminActiveUsersDto(long Dau, long Wau, long Mau);

/// <summary>Retention по first-touch когортам за окно (D1 / D7 / D30). Определение — в <c>GetAdminTrafficStats</c>.</summary>
public sealed record AdminRetentionDto(
    AdminRetentionBucketDto D1,
    AdminRetentionBucketDto D7,
    AdminRetentionBucketDto D30);

/// <summary>
///     Одна retention-метрика: размер когорты (пользователи, чья ПЕРВАЯ сессия попала в окно и кто успел
///     «дожить» N дней), сколько из них вернулось и доля (<c>Rate</c> = returned/cohort, 0 при пустой когорте).
/// </summary>
public sealed record AdminRetentionBucketDto(long CohortSize, long ReturnedCount, double Rate);

/// <summary>Один день окна: сколько НОВЫХ (первая сессия в этот день) и ВЕРНУВШИХСЯ (первая сессия раньше) активных пользователей. Плотный ряд (нулевые дни заполнены).</summary>
public sealed record AdminNewReturningDayDto(DateOnly Date, long NewUsers, long ReturningUsers);

/// <summary>Один день окна: число НАЧАТЫХ сессий по режиму (DRILL/LEARN/MOCK). Плотный ряд (нулевые дни заполнены).</summary>
public sealed record AdminSessionsByModeDayDto(DateOnly Date, long Drill, long Learn, long Mock);
