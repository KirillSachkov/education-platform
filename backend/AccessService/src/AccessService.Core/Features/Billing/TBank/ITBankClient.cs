using AccessService.Core.Features.Billing.TBank.Contracts;

namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// Outbound клиент к T-Bank эквайрингу. Phase F.1.0 — определяем interface;
/// HTTP реализация — F.1.1.
/// </summary>
public interface ITBankClient
{
    /// <summary>Создаёт платёж у T-Bank, возвращает PaymentURL для редиректа юзера.</summary>
    Task<Result<TBankInitResponse, Error>> InitAsync(
        TBankInitRequest request,
        CancellationToken ct = default);

    /// <summary>Опрос статуса платежа (для reconciliation + admin resync).</summary>
    Task<Result<TBankGetStateResponse, Error>> GetStateAsync(
        string paymentId,
        CancellationToken ct = default);

    /// <summary>Возвращает историю платежей провайдера по merchant OrderId.</summary>
    Task<Result<TBankCheckOrderResponse, Error>> CheckOrderAsync(
        string orderId,
        CancellationToken ct = default);

    /// <summary>Возвращает привязанные карты покупателя по стабильному CustomerKey.</summary>
    Task<Result<IReadOnlyList<TBankCard>, Error>> GetCardListAsync(
        string customerKey,
        CancellationToken ct = default);

    /// <summary>
    /// Безредиректное автосписание по сохранённой карте (#614). Вызывается после Init нового
    /// renewal-платежа: <paramref name="paymentId"/> — свежий PaymentId этого платежа,
    /// <paramref name="rebillId"/> — сохранённый recurring-токен родительского платежа.
    /// Использует sweeper из A2b; A2a лишь предоставляет метод.
    /// </summary>
    Task<Result<TBankChargeResponse, Error>> ChargeAsync(
        string paymentId,
        string rebillId,
        CancellationToken ct = default);

    /// <summary>Refund / cancel платежа. Phase F.2 expose endpoint.</summary>
    Task<Result<TBankGetStateResponse, Error>> CancelAsync(
        string paymentId,
        CancellationToken ct = default);
}
