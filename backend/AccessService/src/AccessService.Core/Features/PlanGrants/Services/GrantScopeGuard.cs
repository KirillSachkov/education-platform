using AccessService.Domain;

namespace AccessService.Core.Features.PlanGrants.Services;

/// <summary>
///     Scope-aware dedup для авто-выдаваемых (не оплаченных) grant'ов:
///     <see cref="PlanGrantSource.GITHUB_ORG"/> и <see cref="PlanGrantSource.TELEGRAM_F1"/>.
///
///     Проблема (#687): GitHub-org и Telegram-F3 пути выпускают <b>бессрочный</b>
///     (<c>ExpiresAt == null</c>) grant на привязанный план. Если пользователь уже
///     держит активный grant с тем же или более широким scope — особенно
///     <b>срочный</b> (PURCHASE/TRIAL «доступ на месяц») — наивный per-plan dedup
///     его не видит (другой <c>PlanId</c>) и молча выдаёт второй, бессрочный grant.
///     Срочный «доступ на месяц» превращается в «навсегда» бесплатно: после истечения
///     месячного grant'а union-recalc восстанавливает доступ из выжившего бессрочного.
///
///     Правило: не выдавать авто-grant на план <c>candidate</c>, если у пользователя
///     уже есть ACTIVE grant, scope которого ⊇ scope(candidate). Срочность существующего
///     grant'а не важна — если он покрывает scope, доступ уже выдан корректным каналом
///     (покупка), и дублировать его бессрочной авто-выдачей нельзя.
/// </summary>
public static class GrantScopeGuard
{
    /// <summary>
    ///     Возвращает <c>true</c>, если хотя бы один ACTIVE grant пользователя уже
    ///     покрывает scope плана <paramref name="candidatePlan"/> (тот же план или
    ///     более широкий tier). В этом случае авто-grant выдавать НЕ нужно.
    /// </summary>
    /// <param name="candidatePlan">План, на который собираемся выдать авто-grant.</param>
    /// <param name="activeGrants">Все ACTIVE grant'ы пользователя.</param>
    /// <param name="plansById">Планы существующих grant'ов (для сравнения scope).</param>
    public static bool IsAlreadyCovered(
        Plan candidatePlan,
        IReadOnlyList<PlanGrant> activeGrants,
        IReadOnlyDictionary<Guid, Plan> plansById)
    {
        foreach (PlanGrant grant in activeGrants)
        {
            // Defensive: вызывающие передают уже ACTIVE-only список, но это public reusable
            // guard — не полагаемся на фильтрацию caller'а.
            if (grant.Status != PlanGrantStatus.ACTIVE)
            {
                continue;
            }

            // Тот же план — уже выдан (defensive; per-plan unique index это тоже ловит).
            if (grant.PlanId == candidatePlan.Id)
            {
                return true;
            }

            // Другой план, но его scope ⊇ scope(candidate) → доступ уже покрыт.
            // IsScopeSubset(candidate, existing) == "scope(candidate) ⊆ scope(existing)".
            if (plansById.TryGetValue(grant.PlanId, out Plan? existingPlan)
                && UpgradeCreditCalculator.IsScopeSubset(candidatePlan, existingPlan))
            {
                return true;
            }
        }

        return false;
    }
}
