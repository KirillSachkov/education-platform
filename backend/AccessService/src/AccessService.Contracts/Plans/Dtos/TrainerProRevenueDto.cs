namespace AccessService.Contracts.Plans.Dtos;

/// <summary>
///     Платформенный агрегат выручки по ВСЕМ планам с <c>offer_type = TRAINER_PRO</c> (#623) —
///     для админ-дашборда тренажёра. Подписки/отписки + оценка MRR + динамика выдач по источникам.
///     Кросс-плановый (не по одному плану, в отличие от <see cref="PlanStatsDto"/>); считается
///     Dapper-агрегатом по <c>plan_grants</c> JOIN <c>plans</c>.
/// </summary>
/// <param name="ActiveSubscriptions">Активные гранты TRAINER_PRO (статус ACTIVE).</param>
/// <param name="ActivePayingCount">
///     Из активных — те, за кого реально заплатили (<c>price_paid_cents &gt; 0</c>); отсекает
///     admin-grant / migration / trial, чтобы MRR не считал бесплатные выдачи.
/// </param>
/// <param name="CanceledSubscriptions">Отписки: текущий статус REVOKED или EXPIRED (накопительно).</param>
/// <param name="TotalGrants">Всего грантов TRAINER_PRO за всё время.</param>
/// <param name="NewInPeriod">Новые гранты за окна 7/30/90 дней (по <c>granted_at</c>).</param>
/// <param name="MrrCentsEstimate">
///     Оценка MRR в копейках: <c>SUM(price_paid_cents)</c> по активным грантам. Для месячной подписки
///     ≈ месячная выручка; для иных интервалов — груба (поэтому «оценка», уточняется владельцем).
/// </param>
/// <param name="SourceBreakdown">Разбивка грантов по источнику (PURCHASE / ADMIN_GRANT / …).</param>
/// <param name="Timeseries">Плотный дневной ряд новых грантов за период (по источникам) — для графика.</param>
public sealed record TrainerProRevenueDto(
    long ActiveSubscriptions,
    long ActivePayingCount,
    long CanceledSubscriptions,
    long TotalGrants,
    PlanGrantPeriodCountersDto NewInPeriod,
    long MrrCentsEstimate,
    IReadOnlyList<PlanGrantSourceBreakdownDto> SourceBreakdown,
    IReadOnlyList<PlanStatsTimeseriesPointDto> Timeseries);
