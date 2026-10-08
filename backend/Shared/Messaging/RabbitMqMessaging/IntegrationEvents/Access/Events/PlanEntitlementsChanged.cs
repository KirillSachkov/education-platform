namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Published after a plan mutation changes the entitlement tags of existing grant holders.
/// AccessService consumes it to rebuild affected <c>user-grants:*</c> sets from PostgreSQL.
/// </summary>
public sealed record PlanEntitlementsChanged(
    Guid PlanId,
    DateTimeOffset OccurredAt);
