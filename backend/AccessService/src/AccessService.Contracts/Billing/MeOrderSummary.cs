namespace AccessService.Contracts.Billing;

/// <summary>
/// User-side projection заказа для <c>GET /access/me/orders/</c>. Не содержит
/// полей админ-фокуса (Provider, ExternalProviderRef) — у юзера нет смысла видеть
/// внутренние ссылки T-Bank/ЮKassa. <c>PlanTitle</c> — display name плана (#512),
/// чтобы фронту не тянуть полный публичный каталог планов ради названий;
/// null если план удалён.
/// </summary>
public sealed record MeOrderSummary(
    Guid OrderId,
    Guid PlanId,
    long AmountCents,
    string Currency,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? PaidAt,
    string? FailureReason,
    string? PlanTitle);

public sealed record ListMyOrdersResponse(
    IReadOnlyList<MeOrderSummary> Items,
    int Total,
    int Page,
    int PageSize);
