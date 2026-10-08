namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Audit-row из <c>order_events</c>: каждый webhook / state-transition / admin-action.
/// </summary>
public sealed record AdminOrderEventDto(
    Guid Id,
    string EventType,
    string? PayloadJson,
    Guid? ActorUserId,
    DateTimeOffset CreatedAt,
    string? CorrelationId);
