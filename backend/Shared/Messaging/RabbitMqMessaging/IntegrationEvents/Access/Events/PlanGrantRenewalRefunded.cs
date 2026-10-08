namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// A paid renewal period was rolled back after a full provider refund. Consumers must
/// authoritatively recalculate current access instead of assuming the grant was revoked.
/// </summary>
public sealed record PlanGrantRenewalRefunded(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    Guid RenewalOrderId,
    DateTimeOffset PreviousExpiresAt,
    DateTimeOffset RolledBackExpiresAt,
    DateTimeOffset RefundedAt,
    string? Reason,
    Guid? CanonicalTelegramPlanId = null);
