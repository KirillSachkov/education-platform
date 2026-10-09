using AccessService.Domain;
using ContentAccess;

namespace AccessService.Core.Features.PlanGrants.IntegrationEvents;

/// <summary>
/// Каноничное вычисление набора Redis user-grant тегов для пары (Plan, PlanGrant).
/// Используется Created/Revoked/Expired self-consume handler'ами для DRY-логики
/// и для recalc-on-revoke flow (см. <see cref="SyncContentAccessOnPlanGrantRevokedHandler"/>).
/// </summary>
public static class PlanGrantTagCalculator
{
    /// <summary>
    /// Вычисляет полный набор тегов, которые должны быть в user-grant set
    /// для одного <paramref name="grant"/> (по его <paramref name="plan"/>'у).
    /// Если grant не ACTIVE — возвращает пустой список (revoked/expired не дают тегов).
    /// </summary>
    public static IEnumerable<string> CalculateForGrant(
        PlanGrant grant,
        Plan plan)
    {
        if (grant.Status != PlanGrantStatus.ACTIVE
            || !plan.IsActive
            || plan.ArchivedAt is not null
            || plan.Scope != PlanScope.PLATFORM)
            yield break;

        foreach (string tag in PlanTags(plan))
            yield return tag;

        foreach (string tag in CapabilityTags(plan.Capabilities & ~PlanCapabilities.TRAINER_PRO))
            yield return tag;

    }

    /// <summary>
    /// Вычисляет union тегов для всех <paramref name="grants"/> на соответствующих
    /// <paramref name="plans"/>. Используется при recalc после revoke/expire — чтобы
    /// не задеть теги, всё ещё покрытые другими активными grants пользователя.
    /// </summary>
    public static IReadOnlyList<string> CalculateUnion(
        IReadOnlyList<PlanGrant> grants,
        IReadOnlyDictionary<Guid, Plan> plansByPlanId)
    {
        if (grants.Count == 0)
            return [];

        HashSet<string> set = [];
        foreach (PlanGrant grant in grants)
        {
            if (!plansByPlanId.TryGetValue(grant.PlanId, out Plan? plan))
                continue; // план удалён — теги не материализуем

            foreach (string tag in CalculateForGrant(grant, plan))
                set.Add(tag);
        }

        return [.. set];
    }

    private static IEnumerable<string> PlanTags(Plan plan)
    {
        // FREE-tier deprecated (#358) — legacy archived rows не выпускают новых grants,
        // и существующие AUTO_FREE grants revoked миграцией. Здесь намеренно нет ветки,
        // чтобы случайно «оживший» FREE-grant не получил plan-tag.
        if (plan.Tier is PlanTier.LEARN_ALL or PlanTier.FULL_ALL)
        {
            yield return GrantTags.PlanAll();
            yield break;
        }

        // COURSE / SUBSCRIPTION — one tag per course in the plan's bundle scope (#404).
        foreach (PlanCourse course in plan.Courses)
            yield return GrantTags.PlanCourse(course.CourseId);
    }

    private static IEnumerable<string> CapabilityTags(PlanCapabilities capabilities)
    {
        // Iterate individual flags; алиасы FULL / LEARN_ONLY раскладываются в индивидуальные
        // флаги через PlanCapabilitiesMapper.ToStrings, но здесь нам важно поведение enum.
        foreach (string name in PlanCapabilitiesMapper.ToStrings(capabilities))
            yield return GrantTags.Capability(name);
    }
}