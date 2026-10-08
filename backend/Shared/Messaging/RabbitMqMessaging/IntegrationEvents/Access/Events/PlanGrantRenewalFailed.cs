namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService после каждой неудачной renewal-попытки. <see cref="Stage"/>
/// различает запланированный retry и терминальную ошибку после исчерпания попыток.
/// Consumer получает рассчитанные AccessService даты и не воспроизводит dunning policy.
/// </summary>
/// <param name="GrantId">ID grant'а, который не удалось продлить.</param>
/// <param name="UserId">ID пользователя.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="PlanTier">Тип плана: <c>FREE | LEARN_ALL | FULL_ALL | COURSE | SUBSCRIPTION</c>.</param>
/// <param name="PlanAuthorId">ID автора плана-владельца.</param>
/// <param name="ExpiresAt">Текущий срок grant'а, до которого доступ ещё сохраняется (UTC).</param>
/// <param name="FailureCount">Сколько подряд автосписаний провалилось (= достигнутый порог).</param>
/// <param name="FailureReason">Краткая причина последней неудачи (для текста уведомления / логов).</param>
/// <param name="RenewalOrderId">ID renewal-заказа этой попытки; стабильный correlation для доставки/дедупликации.</param>
/// <param name="FailedAt">Когда попытка завершилась ошибкой (UTC).</param>
/// <param name="NextRetryAt">Следующая попытка (UTC); <c>null</c> для terminal failure.</param>
/// <param name="GraceEndsAt">Конец grace period, после которого доступ закрывается (UTC).</param>
/// <param name="Stage"><see cref="SubscriptionLifecycleStages.RetryScheduled"/> или <see cref="SubscriptionLifecycleStages.TerminalFailure"/>.</param>
public sealed record PlanGrantRenewalFailed(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string PlanTier,
    Guid PlanAuthorId,
    DateTimeOffset? ExpiresAt,
    int FailureCount,
    string? FailureReason,
    Guid? RenewalOrderId = null,
    DateTimeOffset? FailedAt = null,
    DateTimeOffset? NextRetryAt = null,
    DateTimeOffset? GraceEndsAt = null,
    string Stage = SubscriptionLifecycleStages.TerminalFailure)
{
    /// <summary>Attempt number in the current dunning cycle.</summary>
    public int Attempt => FailureCount;

    /// <summary>
    /// Stable across outbox/broker redelivery. Legacy publishers without an order id fall
    /// back to grant scope; lifecycle publishers supply the renewal order id.
    /// </summary>
    public Guid CorrelationId => RenewalOrderId ?? GrantId;
}
