namespace Shared.Email;

/// <summary>
/// Отправитель транзакционных email-сообщений / Transactional email sender.
/// Реализация отвечает за доставку — вызывающий код не делает retry.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Отправляет HTML-письмо. <see cref="EmailSendResult"/> позволяет вызывающему
    /// записать конкретный <c>ErrorCode</c> в <c>notification_deliveries.error_code</c>
    /// (например <c>smtp_550</c>, <c>unisender_5xx</c>) вместо generic
    /// <c>email.send_failed</c>; раньше реализации возвращали <c>bool</c> и swallow'или
    /// exception, и для всех 8 fail'ов за 7d на проде в БД был один и тот же код
    /// <c>channel.exception</c> без полезного детализа.
    /// </summary>
    /// <param name="transactional">
    /// <c>true</c> — письмо системно-критичное (OTP-код входа, сброс пароля): доставляется
    /// в обход списков отписавшихся/недоступных адресов провайдера и БЕЗ ссылки отписки.
    /// Без этого пользователь, когда-либо отписавшийся от рассылок, навсегда теряет вход —
    /// Unisender отклоняет даже OTP с «No valid recipients / unsubscribed». <c>false</c> —
    /// обычная рассылка (digest и т.п.): список отписавшихся уважается (38-ФЗ).
    /// </param>
    Task<EmailSendResult> SendAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        string? textBody,
        bool transactional,
        CancellationToken ct);
}

/// <summary>
/// Результат попытки отправки email. <see cref="ErrorCode"/> — стабильный код для
/// метрик / алертов; <see cref="ErrorDetail"/> — свободный текст для логов.
/// </summary>
public readonly record struct EmailSendResult
{
    private EmailSendResult(bool isSuccess, string? errorCode, string? errorDetail)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorDetail = errorDetail;
    }

    public bool IsSuccess { get; }
    public string? ErrorCode { get; }
    public string? ErrorDetail { get; }

    public static EmailSendResult Success() =>
        new(isSuccess: true, errorCode: null, errorDetail: null);

    public static EmailSendResult Failed(string errorCode, string? errorDetail = null) =>
        new(isSuccess: false, errorCode, errorDetail);
}

/// <summary>
/// Стабильные коды ошибок отправки email. Используются для группировки в метриках
/// (failed deliveries by error_code) и в алертах. Меняются вместе с consumer'ом
/// в NotificationService.
/// </summary>
public static class EmailSendErrorCodes
{
    /// <summary>SMTP отверг адресат: 5xx (mailbox not found / blocked / blacklisted).</summary>
    public const string SMTP_PERMANENT = "smtp_5xx";

    /// <summary>SMTP transient (4xx): host unavailable, deferred, connect timeout.</summary>
    public const string SMTP_TRANSIENT = "smtp_4xx";

    /// <summary>SMTP authentication failure (Username/Password).</summary>
    public const string SMTP_AUTH = "smtp_auth";

    /// <summary>SMTP connect/network error до AUTH (host unreachable).</summary>
    public const string SMTP_CONNECT = "smtp_connect";

    /// <summary>Unisender HTTP API вернул 4xx (bad request, неверный API key).</summary>
    public const string UNISENDER_4XX = "unisender_4xx";

    /// <summary>Unisender HTTP API вернул 5xx (transient backend ошибка).</summary>
    public const string UNISENDER_5XX = "unisender_5xx";

    /// <summary>Сетевая ошибка / DNS / connect timeout до Unisender.</summary>
    public const string UNISENDER_NETWORK = "unisender_network";

    /// <summary>HTTP request timeout (превышен `HttpClient.Timeout`).</summary>
    public const string TIMEOUT = "timeout";

    /// <summary>Прочая ошибка (неклассифицированная) — fallback.</summary>
    public const string UNKNOWN = "unknown";
}
