namespace AccessService.Domain;

/// <summary>
/// Жизненный цикл grant'а: ACTIVE → REVOKED (ручной отзыв) или EXPIRED (по TTL) /
/// PlanGrant lifecycle: ACTIVE → REVOKED (manual) or EXPIRED (TTL elapsed).
/// </summary>
public enum PlanGrantStatus
{
    /// <summary>
    /// Активный grant — доступ открыт / Active grant, access is open.
    /// </summary>
    ACTIVE,

    /// <summary>
    /// Отозван вручную (admin/author) / Manually revoked.
    /// </summary>
    REVOKED,

    /// <summary>
    /// Истёк по TTL / Expired by TTL.
    /// </summary>
    EXPIRED,
}
