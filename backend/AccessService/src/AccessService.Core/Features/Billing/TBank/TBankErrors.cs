namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// Domain error factories для T-Bank интеграции. Codes: <c>tbank.{aspect}.{condition}</c>.
/// </summary>
public static class TBankErrors
{
    public static Error InitFailed(string? errorCode, string? message) =>
        Error.Failure(
            "tbank.init.failed",
            $"T-Bank Init вернул ошибку (errorCode={errorCode ?? "?"}): {message ?? "no message"}");

    public static Error ChargeFailed(string? errorCode, string? message) =>
        Error.Failure(
            "tbank.charge.failed",
            $"T-Bank Charge вернул ошибку (errorCode={errorCode ?? "?"}): {message ?? "no message"}");

    public static Error NetworkError(string detail) =>
        Error.Failure("tbank.network.error", $"Сетевая ошибка при вызове T-Bank: {detail}");

    public static Error InvalidResponse(string detail) =>
        Error.Failure("tbank.response.invalid", $"Некорректный ответ T-Bank: {detail}");

    public static Error CheckOrderProviderError(string? errorCode, string? message) =>
        Error.Failure(
            "tbank.check_order.provider_error",
            $"T-Bank CheckOrder вернул ошибку (errorCode={errorCode ?? "?"}): {message ?? "no message"}");

    public static Error CheckOrderInvalidResponse(string detail) =>
        Error.Failure(
            "tbank.check_order.invalid_response",
            $"Некорректный ответ T-Bank CheckOrder: {detail}");

    public static Error CheckOrderTransportError(string detail) =>
        Error.Failure(
            "tbank.check_order.transport_error",
            $"Сетевая ошибка T-Bank CheckOrder: {detail}");

    public static Error CardListProviderError(string? errorCode, string? message) =>
        Error.Failure(
            "tbank.card_list.provider_error",
            $"T-Bank GetCardList вернул ошибку (errorCode={errorCode ?? "?"}): {message ?? "no message"}");

    public static Error CardListInvalidResponse(string detail) =>
        Error.Failure(
            "tbank.card_list.invalid_response",
            $"Некорректный ответ T-Bank GetCardList: {detail}");

    public static Error CardListTransportError(string detail) =>
        Error.Failure(
            "tbank.card_list.transport_error",
            $"Сетевая ошибка T-Bank GetCardList: {detail}");

    public static Error MissingPaymentUrl() =>
        Error.Failure(
            "tbank.response.no_payment_url",
            "T-Bank вернул Success=true, но без PaymentURL");

    /// <summary>
    /// Init вернул PaymentURL на недоверенном хосте (open-redirect guard, #440). Отдельный код
    /// от <see cref="InvalidResponse"/>, чтобы CreateOrder писал security-аудит <c>INIT_URL_REJECTED</c>,
    /// а не обычный <c>INIT_FAILED</c> (#443). Сам недоверенный URL намеренно НЕ кладём в сообщение.
    /// </summary>
    public static Error UntrustedPaymentUrlHost() =>
        Error.Failure(
            "tbank.response.untrusted_host",
            "T-Bank вернул адрес оплаты на недоверенном хосте — оплата отклонена");
}
