namespace AccessService.Contracts.Billing;

/// <summary>
/// Response для <c>GET /access/orders/{id}/status</c>.
/// <c>Status</c> — строковое представление <c>OrderStatus</c> (PENDING/PAID/FAILED/REFUNDED).
/// </summary>
public sealed record GetOrderStatusResponse(
    Guid OrderId,
    string Status,
    DateTimeOffset? PaidAt,
    string? FailureReason);
