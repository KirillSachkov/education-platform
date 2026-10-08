namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Body для <c>POST /access/admin/orders/{id}/refund</c>. MVP — endpoint возвращает 501;
/// полная реализация в F.2.
/// </summary>
public sealed record RefundOrderRequest(string Reason);
