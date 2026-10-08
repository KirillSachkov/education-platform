namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// The owner resumed automatic renewal before paid access ended. The event exposes the next
/// charge and grace boundaries selected by AccessService so consumers never reimplement policy.
/// </summary>
public sealed record PlanGrantRenewalResumed(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    Guid? RenewalOrderId,
    DateTimeOffset ResumedAt,
    DateTimeOffset NextChargeAt,
    DateTimeOffset GraceEndsAt,
    int Attempt,
    Guid CorrelationId,
    string Stage = SubscriptionLifecycleStages.Resumed);
