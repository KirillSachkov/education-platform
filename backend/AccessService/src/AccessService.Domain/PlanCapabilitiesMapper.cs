namespace AccessService.Domain;

/// <summary>
/// Конвертация bitmask <see cref="PlanCapabilities"/> ↔ список строк имён флагов.
/// API-контракт — массив имён индивидуальных флагов, фронт рендерит как ✓/✗ список.
///
/// Используем явный canonical-маппинг (а не <c>Enum.GetName</c>) потому что в
/// <see cref="PlanCapabilities"/> есть алиасы (<c>FULL</c>, <c>LEARN_ONLY</c>),
/// которые совпадают по значению с individual-флагами и могут быть возвращены
/// вместо имени канонического флага.
///
/// Domain-слой намеренно — маппинг не зависит от инфраструктуры и переиспользуется
/// из 3+ use-case файлов (GetMyPlans, GetPublicPlans, Update).
/// </summary>
public static class PlanCapabilitiesMapper
{
    private static readonly (PlanCapabilities Flag, string Name)[] CanonicalFlags =
    [
        (PlanCapabilities.VIEW_MATERIALS, "VIEW_MATERIALS"),
        (PlanCapabilities.SUBMIT_ISSUES, "SUBMIT_ISSUES"),
        (PlanCapabilities.CODE_REVIEW, "CODE_REVIEW"),
        (PlanCapabilities.COMMUNITY_ACCESS, "COMMUNITY_ACCESS"),
        (PlanCapabilities.LIVE_CALLS, "LIVE_CALLS"),
        (PlanCapabilities.JOB_SUPPORT, "JOB_SUPPORT"),
        (PlanCapabilities.TRAINER_PRO, "TRAINER_PRO"),
    ];

    public static IReadOnlyList<string> ToStrings(PlanCapabilities capabilities)
    {
        List<string> result = [];
        foreach ((PlanCapabilities flag, string name) in CanonicalFlags)
        {
            if ((capabilities & flag) == flag)
            {
                result.Add(name);
            }
        }

        return result;
    }

    public static PlanCapabilities FromStrings(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            return PlanCapabilities.NONE;
        }

        PlanCapabilities result = PlanCapabilities.NONE;
        foreach (string name in names)
        {
            foreach ((PlanCapabilities flag, string canonical) in CanonicalFlags)
            {
                if (string.Equals(name, canonical, StringComparison.OrdinalIgnoreCase))
                {
                    result |= flag;
                    break;
                }
            }
        }

        return result;
    }
}
