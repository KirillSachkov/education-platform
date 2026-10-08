using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Shared.Email;

/// <summary>
/// SMTP-реализация <see cref="IEmailSender"/> поверх MailKit. Используется в dev (Mailpit).
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
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
        // `transactional` не применим к SMTP (dev/Mailpit) — у SMTP нет списка отписавшихся,
        // bypass актуален только для Unisender Go (prod). Параметр принимается ради контракта.
        _ = transactional;
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.FromName, _options.From));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var builder = new BodyBuilder
            {
                HtmlBody = htmlBody,
                TextBody = textBody ?? string.Empty,
            };
            message.Body = builder.ToMessageBody();

            using var client = new SmtpClient();

            SecureSocketOptions tlsOption = _options.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.Auto;

            await client.ConnectAsync(_options.Host, _options.Port, tlsOption, ct);

            if (!string.IsNullOrEmpty(_options.Username))
                await client.AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, ct);

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            _logger.LogInformation("Email отправлен на {Email}", toEmail);
            return EmailSendResult.Success();
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP auth failure отправки email на {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.SMTP_AUTH, ex.Message);
        }
        catch (SmtpCommandException ex)
        {
            // 5xx (permanent) vs 4xx (transient) — для retry/bounce-handling в delivery_log.
            int status = (int)ex.StatusCode;
            string code = status >= 500 ? EmailSendErrorCodes.SMTP_PERMANENT : EmailSendErrorCodes.SMTP_TRANSIENT;
            _logger.LogWarning(ex, "SMTP command error {Status} отправки email на {Email}", status, toEmail);
            return EmailSendResult.Failed(code, $"smtp_{status}: {ex.Message}");
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogWarning(ex, "SMTP protocol error отправки email на {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.SMTP_TRANSIENT, ex.Message);
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "SMTP timeout отправки email на {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.TIMEOUT, ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            // socket errors / connect failures — `IOException` (без подключения к SMTP).
            _logger.LogWarning(ex, "SMTP connect/IO error отправки email на {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.SMTP_CONNECT, ex.Message);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Неклассифицированная ошибка отправки email на {Email}", toEmail);
            return EmailSendResult.Failed(EmailSendErrorCodes.UNKNOWN, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
