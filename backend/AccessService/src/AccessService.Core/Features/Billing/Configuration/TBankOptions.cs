namespace AccessService.Core.Features.Billing.Configuration;

/// <summary>
/// Конфигурация T-Bank эквайринга. Заполняется из секции <c>TBank:</c> в appsettings
/// + env vars <c>TBANK__TERMINALKEY</c> / <c>TBANK__PASSWORD</c>.
///
/// Test и production отличаются только credentials — host тот же
/// (<c>https://securepay.tinkoff.ru/v2/</c>). Phase F.1.0 — issue #102.
///
/// **Опциональны** — если <see cref="TerminalKey"/> или <see cref="Password"/>
/// пустые, billing отключён: <c>POST /access/orders/</c> возвращает
/// <c>billing.not_configured</c>, webhook'и игнорируются. AccessService при этом
/// стартует штатно (rest of the service не зависит от T-Bank). Позволяет
/// безопасно деплоить код в окружения без TBank credentials (prod до cutover).
/// </summary>
public sealed class TBankOptions
{
    public const string SECTION_NAME = "TBank";

    /// <summary>Идентификатор терминала, выданный T-Bank в ЛК. Пустой = billing disabled.</summary>
    public string TerminalKey { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret для подписи запросов (sha256 token). Никогда не логировать,
    /// никогда не возвращать в API. Пустой = billing disabled.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// True если T-Bank credentials настроены и billing активен. Используется в
    /// <c>CreateOrderHandler</c> и <c>TBankWebhookHandler</c> как guard перед любым
    /// вызовом T-Bank API. Если false — endpoints возвращают
    /// <c>billing.not_configured</c>.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(TerminalKey)
        && !string.IsNullOrWhiteSpace(Password);

    /// <summary>Базовый URL T-Bank API. Default: <c>https://securepay.tinkoff.ru/v2/</c>.</summary>
    public string BaseUrl { get; set; } = "https://securepay.tinkoff.ru/v2/";

    /// <summary>URL нашего webhook endpoint'а — передаётся в T-Bank Init как NotificationURL.</summary>
    public string NotificationUrl { get; set; } = string.Empty;

    /// <summary>URL для редиректа на success — T-Bank подставит <c>{OrderId}</c>.</summary>
    public string SuccessUrl { get; set; } = string.Empty;

    /// <summary>URL для редиректа на fail.</summary>
    public string FailUrl { get; set; } = string.Empty;

    /// <summary>HTTP timeout для вызовов T-Bank API. Default 10 сек.</summary>
    public int HttpTimeoutSeconds { get; set; } = 10;

    /// <summary>Receipt-секция для 54-ФЗ (CloudKassir).</summary>
    public TBankReceiptOptions Receipt { get; set; } = new();
}

public sealed class TBankReceiptOptions
{
    /// <summary>
    /// Налоговый режим ИП: <c>usn_income</c>, <c>usn_income_outcome</c>, <c>patent</c>,
    /// <c>osn</c>, <c>esn</c>, <c>envd</c>. Для ИП на УСН (доходы) default — <c>usn_income</c>.
    /// </summary>
    public string Taxation { get; set; } = "usn_income";

    /// <summary>
    /// Fallback email если у юзера в AuthService не указан. Юзер не получит чек на почту,
    /// но flow не падает.
    /// </summary>
    public string DefaultEmail { get; set; } = "no-receipt@platform.local";
}
