namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService после успешного автосписания по подписке: фоновая задача
/// <c>RecurringChargesSweeper</c> провела безредиректный charge по сохранённому RebillId,
/// продлила grant (<c>PlanGrant.Renew</c>) и сдвинула <c>ExpiresAt</c> на новый период (#614).
/// Семантически — продление существующего ACTIVE-grant'а: consumer'ы (уведомления о
/// продлении подписки и т.п.) узнают, что доступ продлён до нового срока. Зеркалит контракт
/// <see cref="PlanGrantExpired"/>.
/// Published by AccessService when a subscription grant was auto-renewed via a recurring charge.
/// </summary>
/// <param name="GrantId">ID продлённого PlanGrant.</param>
/// <param name="UserId">ID пользователя.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="PlanTier">Тип плана: <c>FREE | LEARN_ALL | FULL_ALL | COURSE | SUBSCRIPTION</c>.</param>
/// <param name="PlanAuthorId">ID автора плана-владельца. Entitlement scope определяется планом/курсами.</param>
/// <param name="ExpiresAt">Новый срок действия grant'а после продления (UTC).</param>
/// <param name="RenewalOrderId">ID renewal-заказа; стабильный correlation для доставки/дедупликации.</param>
/// <param name="RenewedAt">Когда renewal был применён (UTC).</param>
/// <param name="PreviousExpiresAt">Предыдущая граница оплаченного доступа (UTC).</param>
/// <param name="NextChargeAt">Следующая рассчитанная дата списания (UTC).</param>
/// <param name="GraceEndsAt">Новая граница grace period (UTC).</param>
/// <param name="Attempt">Номер успешной попытки в текущем dunning-цикле, начиная с 1.</param>
/// <param name="Stage">Стадия lifecycle; для этого события <see cref="SubscriptionLifecycleStages.Renewed"/>.</param>
public sealed record PlanGrantRenewed(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    DateTimeOffset ExpiresAt,
    Guid? RenewalOrderId = null,
    DateTimeOffset? RenewedAt = null,
    DateTimeOffset? PreviousExpiresAt = null,
    DateTimeOffset? NextChargeAt = null,
    DateTimeOffset? GraceEndsAt = null,
    int Attempt = 1,
    string Stage = SubscriptionLifecycleStages.Renewed)
{
    /// <summary>
    /// Stable across outbox/broker redelivery. Legacy publishers without an order id fall
    /// back to grant scope; lifecycle publishers supply the renewal order id.
    /// </summary>
    public Guid CorrelationId => RenewalOrderId ?? GrantId;
}
