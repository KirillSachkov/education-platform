namespace AccessService.Contracts.Billing;

/// <summary>Рантайм-флаг приёма прямой оплаты (T-Bank). Читается фронтом анонимно.</summary>
public sealed record BillingConfigDto(bool IsEnabled);

/// <summary>Тело <c>PATCH /access/billing-config</c> — admin-флип тумблера.</summary>
public sealed record UpdateBillingConfigRequest(bool IsEnabled);
