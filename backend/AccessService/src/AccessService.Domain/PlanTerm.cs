namespace AccessService.Domain;

/// <summary>
/// Тип срока действия плана. <see cref="LIFETIME"/> grant'ы не истекают;
/// <see cref="RECURRING"/> потребуют шедулера expiry (dormant, ждёт payment).
/// Plan term kind. <see cref="LIFETIME"/> grants never expire;
/// <see cref="RECURRING"/> would require scheduled expiry (dormant, payment integration).
/// </summary>
public enum PlanTermKind
{
    LIFETIME,
    RECURRING,
}

/// <summary>
/// Срок действия плана / Plan term.
/// </summary>
public sealed record PlanTerm(PlanTermKind Kind, int? RecurringIntervalDays = null)
{
    public static PlanTerm Lifetime => new(PlanTermKind.LIFETIME);

    /// <summary>
    /// Периодический срок для подписки (#614): grant продлевается каждые
    /// <paramref name="intervalDays"/> дней через автосписание. Валидация &gt; 0 — в
    /// <see cref="Plan.Create"/> (<c>SubscriptionRequiresRecurringTerm</c>).
    /// </summary>
    public static PlanTerm Recurring(int intervalDays) =>
        new(PlanTermKind.RECURRING, intervalDays);
}
