namespace AccessService.Contracts.Billing;

/// <summary>
/// Request body для <c>POST /access/orders/</c>. Frontend шлёт только PlanId —
/// цена снэпшотится из <c>Plan.PriceCents</c> на сервере (защита от tampering).
/// Idempotency-Key передаётся header'ом, не в body.
/// </summary>
public sealed record CreateOrderRequest(Guid PlanId);
