namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Paginated результат admin-search'а заказов.
/// </summary>
public sealed record ListAdminOrdersResponse(
    IReadOnlyList<AdminOrderSummary> Items,
    int Total,
    int Page,
    int PageSize);
