namespace Shared.Email;

/// <summary>
/// Настройки отправки email / Email sending options.
/// Единая секция <c>"Email"</c> в <c>appsettings.{Environment}.json</c> для всех сервисов.
/// </summary>
public sealed class EmailOptions
{
    public const string SECTION_NAME = "Email";

    /// <summary>
    /// Адрес отправителя (e.g. <c>noreply@sachkov-learn.net</c>).
    /// </summary>
    public string From { get; init; } = string.Empty;

    /// <summary>
    /// Имя отправителя (e.g. <c>Sachkov Learn</c>).
    /// </summary>
    public string FromName { get; init; } = string.Empty;

    /// <summary>
    /// SMTP host (dev — Mailpit).
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// SMTP port (dev — 1025).
    /// </summary>
    public int Port { get; init; } = 1025;

    /// <summary>
    /// Использовать ли TLS для SMTP.
    /// </summary>
    public bool UseSsl { get; init; }

    /// <summary>
    /// SMTP username (опционально).
    /// </summary>
    public string? Username { get; init; }

    /// <summary>
    /// SMTP password (опционально).
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Unisender Go HTTP API key (prod). Если задан — используется HTTP API вместо SMTP.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Использовать ли HTTP API вместо SMTP (true, если задан <see cref="ApiKey"/>).
    /// </summary>
    public bool UseHttpApi => !string.IsNullOrEmpty(ApiKey);

    /// <summary>
    /// Разрешить транзакционным письмам (OTP, сброс пароля) обходить статус отписки
    /// Unisender (поля <c>force_send</c> + <c>skip_unsubscribe</c>). По умолчанию <c>false</c>:
    /// Unisender принимает эти поля ТОЛЬКО если их вайтлистнула техподдержка на аккаунте
    /// (тикет UNI-616541), иначе реджектит весь запрос 400-й (code 1588) — и OTP перестаёт
    /// уходить ВСЕМ. На prod включено через <c>appsettings.Production.json</c> (2026-06-15);
    /// для других окружений включать (env <c>Email__BypassUnsubscribeForTransactional=true</c>)
    /// только ПОСЛЕ подтверждения от поддержки. Пока выключено — отписавшиеся не получат код
    /// (известное ограничение), но штатные отправки работают.
    /// </summary>
    public bool BypassUnsubscribeForTransactional { get; init; }
}
