namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Lightweight DTO для списка заказов в admin-панели. Не содержит full audit log —
/// для деталей вызывать <c>GET /access/admin/orders/{id}</c>.
/// </summary>
public sealed record AdminOrderSummary(
    Guid OrderId,
    Guid UserId,
    Guid PlanId,
    long AmountCents,
    string Currency,
    string Status,
    string? Provider,
    string? ExternalProviderRef,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? FailureReason,
    string? CorrelationId);
