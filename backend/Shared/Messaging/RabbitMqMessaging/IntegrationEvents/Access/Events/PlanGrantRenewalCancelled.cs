namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// The owner disabled automatic renewal while retaining already-paid access through
/// <paramref name="AccessEndsAt"/>. No future charge may be created until renewal is resumed.
/// </summary>
public sealed record PlanGrantRenewalCancelled(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    Guid? RenewalOrderId,
    DateTimeOffset CancelledAt,
    DateTimeOffset AccessEndsAt,
    int Attempt,
    Guid CorrelationId,
    string Stage = SubscriptionLifecycleStages.Cancelled);
