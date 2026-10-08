namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Body для <c>POST /access/admin/orders/{id}/grant-manually</c>. Reason обязательно
/// для аудита (банк-перевод вне нашей системы / клиентское urgent-предоставление доступа).
/// </summary>
public sealed record GrantManuallyRequest(string Reason);
