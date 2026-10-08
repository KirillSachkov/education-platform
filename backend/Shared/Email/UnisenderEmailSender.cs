using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.Email;

/// <summary>
/// HTTP-реализация <see cref="IEmailSender"/> поверх Unisender Go API. Используется в prod
/// (VPS блокирует SMTP-порты, поэтому HTTP — единственный вариант).
/// </summary>
public sealed class UnisenderEmailSender : IEmailSender
{
    private const string API_URL = "https://go2.unisender.ru/ru/transactional/api/v1/email/send.json";

    private readonly HttpClient _httpClient;
    private readonly EmailOptions _options;
    private readonly ILogger<UnisenderEmailSender> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public UnisenderEmailSender(
        HttpClient httpClient,
        IOptions<EmailOptions> options,
        ILogger<UnisenderEmailSender> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(
        string toEmail,
        string toName,
        string subject,
        string htmlBody,
        string? textBody,
        bool transactional,
        CancellationToken ct)
    {
        try
        {
            // Транзакционные письма (OTP, сброс пароля) при включённом флаге шлются с
            // force_send=1 (игнор статуса «отписан»/недоступен при отправке) + skip_unsubscribe=1
            // (без блока ссылки отписки). Это ровно те два поля, которые техподдержка Unisender
            // вайтлистнула на аккаунте (тикет UNI-616541, включено 2026-06-15) — НЕ bypass_*
            // (их аккаунт не разрешал). Без вайтлиста Unisender реджектит весь запрос 400
            // (code 1588) и OTP перестаёт уходить ВСЕМ — поэтому поля шлём только при явном
            // Email__BypassUnsubscribeForTransactional=true. Маркетинг (transactional=false)
            // отписку уважает всегда (38-ФЗ).
            int? bypass = transactional && _options.BypassUnsubscribeForTransactional ? 1 : null;

            var request = new UnisenderRequest
            {
                Message = new UnisenderMessage
                {
                    Recipients = [new Recipient { Email = toEmail, Name = toName }],
                    Subject = subject,
                    FromEmail = _options.From,
                    FromName = _options.FromName,
                    Body = new EmailBody
                    {
                        Html = htmlBody,
                        Plaintext = textBody ?? string.Empty,
                    },
                    ForceSend = bypass,
                    SkipUnsubscribe = bypass,
                },
            };

            using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
                API_URL, request, _jsonOptions, ct);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                int status = (int)response.StatusCode;
                string code = status >= 500
                    ? EmailSendErrorCodes.UNISENDER_5XX
                    : EmailSendErrorCodes.UNISENDER_4XX;

                _logger.LogError(
                    "Unisender API error {StatusCode} for {Email}: {Body}",
                    status, toEmail, body);

                // Truncate body to keep error_detail compact in DB (1KB max).
                string detail = body.Length > 1024 ? body[..1024] : body;
                return EmailSendResult.Failed(code, $"unisender_{status}: {detail}");
            }

            _logger.LogInformation("Email отправлен на {Email}", toEmail);
            return EmailSendResult.Success();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Unisender request timeout for {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.TIMEOUT, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            // Сетевая ошибка / DNS / connection refused.
            _logger.LogWarning(ex, "Unisender network error for {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.UNISENDER_NETWORK, ex.Message);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Неклассифицированная ошибка отправки Unisender для {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.UNKNOWN, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // --- Request DTOs ---

    private sealed class UnisenderRequest
    {
        public UnisenderMessage Message { get; init; } = null!;
    }

    private sealed class UnisenderMessage
    {
        public List<Recipient> Recipients { get; init; } = [];
        public string Subject { get; init; } = string.Empty;
        public string FromEmail { get; init; } = string.Empty;
        public string FromName { get; init; } = string.Empty;
        public EmailBody Body { get; init; } = null!;

        // Override-поля Unisender Go (см. SendAsync). int? — null опускается при сериализации
        // (DefaultIgnoreCondition.WhenWritingNull), поэтому обычные рассылки шлются
        // байт-в-байт как раньше; выставляются только для transactional=true при включённом
        // флаге. force_send — игнор статуса адреса; skip_unsubscribe — без блока отписки.
        public int? ForceSend { get; init; }
        public int? SkipUnsubscribe { get; init; }
    }

    private sealed class Recipient
    {
        public string Email { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
    }

    private sealed class EmailBody
    {
        public string Html { get; init; } = string.Empty;
        public string Plaintext { get; init; } = string.Empty;
    }
}
