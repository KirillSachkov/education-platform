namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Результат manual resync'а: до/после статусы. Если статус не изменился —
/// previous == new (например, T-Bank ещё в AUTHORIZING).
/// </summary>
public sealed record ResyncOrderResponse(string PreviousStatus, string NewStatus);
