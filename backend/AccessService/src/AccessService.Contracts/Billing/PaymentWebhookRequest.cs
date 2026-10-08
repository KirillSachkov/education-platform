namespace AccessService.Contracts.Billing;

/// <summary>
/// Provider-agnostic webhook payload от платёжного провайдера. Конкретные
/// провайдеры (ЮKassa, Stripe) шлют свой формат → платформа должна нормализовать
/// в этот контракт через адаптер per-provider (TODO).
/// </summary>
/// <param name="OrderId">ID нашего <see cref="Order"/> (frontend сохранил при checkout).</param>
/// <param name="ExternalRef">ID транзакции у провайдера для idempotency.</param>
/// <param name="Status">AUTHORIZED | PAID | FAILED | REFUNDED.</param>
/// <param name="Reason">Опциональная причина (для FAILED/REFUNDED).</param>
/// <param name="RebillId">
/// Recurring-токен провайдера для безредиректных автосписаний (T-Bank RebillId, #614).
/// Присутствует только в CONFIRMED-уведомлении о родительском платеже подписки; <c>null</c>
/// для обычных разовых платежей. Сохраняется на grant'е подписочного плана.
/// </param>
public sealed record PaymentWebhookRequest(
    Guid OrderId,
    string ExternalRef,
    string Status,
    string? Reason,
    string? RebillId = null);
