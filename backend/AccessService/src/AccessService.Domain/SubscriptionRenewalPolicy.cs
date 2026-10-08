namespace AccessService.Domain;

/// <summary>
/// Canonical recurring billing schedule for issue #746. These values are business policy,
/// not runtime tuning knobs: changing them requires coordinated product, notification and
/// customer-facing updates.
/// </summary>
public static class SubscriptionRenewalPolicy
{
    public const int MAX_ATTEMPTS = 3;

    public static readonly TimeSpan ChargeLeadTime = TimeSpan.FromHours(24);

    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(72);

    public static DateTimeOffset FirstChargeAt(DateTimeOffset expiresAt) =>
        expiresAt.Subtract(ChargeLeadTime);

    public static DateTimeOffset GraceEndsAt(DateTimeOffset expiresAt) =>
        expiresAt.Add(GracePeriod);

    /// <summary>
    /// Returns the next scheduled attempt after <paramref name="completedFailureCount"/>.
    /// Attempt 1 is T-24h, attempt 2 is T, attempt 3 is T+48h.
    /// </summary>
    public static DateTimeOffset? NextRetryAt(
        DateTimeOffset expiresAt,
        int completedFailureCount) => completedFailureCount switch
        {
            1 => expiresAt,
            2 => expiresAt.AddHours(48),
            _ => null,
        };
}
