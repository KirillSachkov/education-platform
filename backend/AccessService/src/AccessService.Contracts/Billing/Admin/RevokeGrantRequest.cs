namespace AccessService.Contracts.Billing.Admin;

/// <summary>
/// Body для <c>POST /access/admin/orders/{id}/revoke-grant</c>. Reason обязательно — для
/// аудита (нарушение Terms / chargeback / mistake).
/// </summary>
public sealed record RevokeGrantRequest(string Reason);
