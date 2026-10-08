namespace AccessService.Domain;

/// <summary>
/// Lifecycle статус <see cref="Order"/>. PENDING → PAID или FAILED. PAID → REFUNDED
/// (chargeback / явный refund).
/// </summary>
public enum OrderStatus
{
    /// <summary>Заказ создан, ждёт подтверждения оплаты от провайдера.</summary>
    PENDING,

    /// <summary>Провайдер подтвердил оплату → выпущен <c>PlanGrant</c>.</summary>
    PAID,

    /// <summary>Провайдер вернул отказ (карта отклонена / истёк payment session).</summary>
    FAILED,

    /// <summary>Возврат средств (refund / chargeback) → grant отозван.</summary>
    REFUNDED,
}
