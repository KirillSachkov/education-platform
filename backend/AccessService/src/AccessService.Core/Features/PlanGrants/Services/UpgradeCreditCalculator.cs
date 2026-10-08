using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Core.Features.PlanGrants.Services;

/// <summary>
///     Реализация subset-rule из <see cref="IUpgradeCreditCalculator"/>. Загружает
///     все grants пользователя, группирует их по плану
///     (анти-double-count), для каждого плана проверяет <see cref="IsScopeSubset"/>
///     и кредитует <c>price_paid_cents</c>; для неоплаченных ACTIVE grants (инвайт /
///     Telegram-бот / GitHub-org / admin-grant) — текущую эффективную цену покрытого
///     плана (#486). Capping: credit ≤ original_price.
/// </summary>
public sealed class UpgradeCreditCalculator : IUpgradeCreditCalculator
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IPlansRepository _plans;
    private readonly TimeProvider _time;

    public UpgradeCreditCalculator(IPlanGrantsRepository grants, IPlansRepository plans, TimeProvider time)
    {
        _grants = grants;
        _plans = plans;
        _time = time;
    }

    public async Task<UpgradeQuote> CalculateAsync(
        Guid userId,
        Plan targetPlan,
        CancellationToken ct)
    {
        DateTimeOffset now = _time.GetUtcNow();

        // Базовая цена для quote — эффективная (с учётом активной акции), чтобы показанная
        // на pricing-странице сумма совпадала с тем, что реально спишет Order при покупке.
        // Paid trial может зафиксировать более старую базу lifetime-доступа — она применится
        // ниже, когда найдём credit-source.
        long? effectivePrice = targetPlan.EffectivePriceCents(now);

        // 1. Все grants пользователя. EXPIRED грант учитывается как «уже заплачено» (TTL
        //    истёк, но деньги были внесены), а REVOKED — НЕТ (owner decision: refunded /
        //    отозванный грант не даёт upgrade-credit, иначе после refund'а юзер получил бы
        //    скидку за деньги, которые ему вернули). IsOwned считается только по ACTIVE.
        IReadOnlyList<PlanGrant> userGrants = await _grants.GetManyByAsync(
            g => g.UserId == userId,
            ct);

        if (userGrants.Count == 0)
        {
            return new UpgradeQuote(
                effectivePrice,
                CreditCents: 0,
                FinalPriceCents: effectivePrice,
                IsOwned: false,
                Sources: []);
        }

        // 2. Подгружаем планы grants'ов одним батчем — нужны для scope-check.
        Guid[] grantedPlanIds = userGrants.Select(g => g.PlanId).Distinct().ToArray();
        IReadOnlyList<Plan> grantedPlans = await _plans.GetManyByAsync(
            p => grantedPlanIds.Contains(p.Id),
            ct);
        Dictionary<Guid, Plan> plansById = grantedPlans.ToDictionary(p => p.Id);

        // 3. IsOwned — есть ли уже ACTIVE grant на target или non-trial grant, полностью
        // покрывающий target. Trial FULL_ALL даёт временный доступ, но не блокирует покупку
        // lifetime-плана (#580).
        bool isOwned = userGrants.Any(g =>
            g.Status == PlanGrantStatus.ACTIVE
            && (g.PlanId == targetPlan.Id
                || (plansById.TryGetValue(g.PlanId, out Plan? grantPlan)
                    && !grantPlan.IsTrial
                    && IsScopeSubset(targetPlan, grantPlan))));

        if (isOwned)
        {
            // Если уже куплен — credit не считаем, фронт скрывает payment-кнопку.
            return new UpgradeQuote(
                effectivePrice,
                CreditCents: 0,
                FinalPriceCents: effectivePrice,
                IsOwned: true,
                Sources: []);
        }

        // 4. Считаем credit: scope(grant.plan) ⊆ scope(target). Grants группируются по плану,
        //    чтобы пара grants на один план (оплаченный + выданный ботом) не дала двойной credit.
        List<UpgradeCreditSource> sources = [];
        List<long> upgradeBasePriceSnapshots = [];
        foreach (IGrouping<Guid, PlanGrant> byPlan in userGrants
                     .Where(g => g.Status != PlanGrantStatus.REVOKED)
                     .GroupBy(g => g.PlanId))
        {
            if (!plansById.TryGetValue(byPlan.Key, out Plan? grantPlan)) continue;

            // Не зачёт self-upgrade на тот же план (isOwned уже отрезал ACTIVE-self).
            if (!IsScopeSubset(grantPlan, targetPlan)) continue;

            // Оплаченные grants кредитуют фактически внесённую сумму. REVOKED (включая
            // refunded) отрезан выше — owner decision; EXPIRED продолжает учитываться
            // (TTL истёк, но оплата состоялась и не возвращалась).
            long paidSum = byPlan.Sum(g => Math.Max(g.PricePaidCents ?? 0, 0));

            long credit;
            Guid sourceGrantId;
            if (paidSum > 0)
            {
                credit = paidSum;
                sourceGrantId = byPlan.First(g => g.PricePaidCents is > 0).Id;
                if (targetPlan.Tier == PlanTier.FULL_ALL)
                {
                    upgradeBasePriceSnapshots.AddRange(byPlan
                        .Where(g => g.PricePaidCents is > 0 && g.UpgradeBasePriceCents is > 0)
                        .Select(g => g.UpgradeBasePriceCents!.Value));
                }
            }
            else
            {
                // Fallback (#486): grant без оплаты (инвайт / Telegram-бот / GitHub-org /
                // admin-grant) кредитует ТЕКУЩУЮ эффективную цену покрытого плана — юзер
                // уже владеет этим scope'ом и должен доплатить только разницу (обещание
                // FAQ на /pricing). Только ACTIVE: истёкший неоплаченный grant ничем не
                // владеет и credit не даёт.
                PlanGrant? active = byPlan.FirstOrDefault(g => g.Status == PlanGrantStatus.ACTIVE);
                if (active is null) continue;

                long? grantPlanPrice = grantPlan.EffectivePriceCents(now);
                if (grantPlanPrice is not long fallback || fallback <= 0) continue;

                credit = fallback;
                sourceGrantId = active.Id;
            }

            sources.Add(new UpgradeCreditSource(
                sourceGrantId,
                grantPlan.Id,
                grantPlan.DisplayName.Value,
                grantPlan.Tier,
                credit));
        }

        long rawCredit = sources.Sum(s => s.CreditCents);
        long? originalPrice = ApplyUpgradeBasePriceSnapshots(effectivePrice, upgradeBasePriceSnapshots);

        long? finalPrice;
        long cappedCredit;
        if (originalPrice is long original)
        {
            cappedCredit = Math.Min(rawCredit, original);
            finalPrice = Math.Max(0, original - cappedCredit);
        }
        else
        {
            // У плана нет цены (TBD) — credit лишён смысла, но возвращаем sum для прозрачности.
            cappedCredit = 0;
            finalPrice = null;
        }

        return new UpgradeQuote(
            originalPrice,
            cappedCredit,
            finalPrice,
            IsOwned: false,
            sources);
    }

    private static long? ApplyUpgradeBasePriceSnapshots(long? effectivePrice, IReadOnlyList<long> snapshots)
    {
        if (effectivePrice is not long current || snapshots.Count == 0)
        {
            return effectivePrice;
        }

        return Math.Min(current, snapshots.Min());
    }

    /// <summary>
    ///     Проверяет, что scope <paramref name="grantedPlan"/> полностью покрывается
    ///     <paramref name="targetPlan"/>. Реализует tier-hierarchy из design doc.
    /// </summary>
    /// <remarks>
    ///     FULL_ALL / LEARN_ALL имеют global scope; COURSE сравнивается по courseIds.
    ///     <c>public static</c> для доступа из unit-тестов; чистая функция от
    ///     (<see cref="Plan.Tier"/>, <see cref="Plan.Capabilities"/>, <see cref="Plan.GetCourseIds"/>).
    /// </remarks>
    public static bool IsScopeSubset(Plan grantedPlan, Plan targetPlan)
    {
        // Самопокрытие исключается (одна и та же пара tier+plan не должна credit'ить себя
        // — это означало бы re-purchase, что не наша задача в Phase 2).
        if (grantedPlan.Id == targetPlan.Id) return false;

        return targetPlan.Tier switch
        {
            // FULL_ALL покрывает всю платформу + ВСЕ capabilities + любые courseIds.
            PlanTier.FULL_ALL => true,

            // LEARN_ALL покрывает только VIEW_MATERIALS scope платформы. Subset — те grants,
            // у которых capabilities ⊆ VIEW_MATERIALS (т.е. только VIEW_MATERIALS) и tier
            // — FREE/LEARN_ALL/COURSE-with-view-only.
            PlanTier.LEARN_ALL =>
                grantedPlan.Capabilities == PlanCapabilities.VIEW_MATERIALS,

            // COURSE bundle (#404): granted COURSE credit'ится, если его набор курсов —
            // подмножество target'а (target покрывает всё, что покрывал granted) И
            // capabilities ⊆ target.capabilities. Множества берутся из Plan.GetCourseIds().
            // LEARN_ALL/FULL_ALL не subset (они шире).
            PlanTier.COURSE => grantedPlan.Tier switch
            {
                PlanTier.COURSE =>
                    IsCourseSetSubset(grantedPlan, targetPlan)
                    && (grantedPlan.Capabilities & targetPlan.Capabilities) == grantedPlan.Capabilities,
                _ => false,
            },

            // FREE / SUBSCRIPTION — на FREE никогда не апгрейдятся (платить нечего за credit'ом),
            // SUBSCRIPTION — Phase 3.
            _ => false,
        };
    }

    private static bool IsCourseSetSubset(Plan grantedPlan, Plan targetPlan)
    {
        IReadOnlyList<Guid> granted = grantedPlan.GetCourseIds();
        if (granted.Count == 0) return false;

        HashSet<Guid> targetCourses = [.. targetPlan.GetCourseIds()];
        return targetCourses.IsSupersetOf(granted);
    }
}
