using System.Net;
using System.Security.Cryptography;
using System.Text;
using Shared.Email;

namespace AuthService.Core.Services;

/// <summary>
/// Facade над <see cref="IEmailSender"/>: рендерит HTML/text шаблоны для OTP и reset password.
/// Сам SMTP/HTTP берёт из общего Shared.Email — реализация выбирается автоматически.
/// </summary>
public sealed class AuthEmailSender : IAuthEmailSender
{
    private readonly IEmailSender _sender;
    private readonly ILogger<AuthEmailSender> _logger;

    public AuthEmailSender(IEmailSender sender, ILogger<AuthEmailSender> logger)
    {
        _sender = sender;
        _logger = logger;
    }

    public async Task<bool> SendOtpAsync(string toEmail, string code, CancellationToken ct)
    {
        string subject = "Ваш код подтверждения";
        string text = $"Ваш код подтверждения: {code}\n\nДействителен 5 минут.";
        string safeCode = WebUtility.HtmlEncode(code);
        string html = $"""
            <!DOCTYPE html>
            <html><body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:20px;">
              <h2 style="color:#1a1a1a;">Код подтверждения</h2>
              <p style="color:#4a4a4a;">Ваш код:</p>
              <div style="font-size:28px;font-weight:bold;letter-spacing:4px;padding:16px;background:#f5f5f5;border-radius:8px;text-align:center;">{safeCode}</div>
              <p style="color:#999;font-size:12px;margin-top:30px;">Код действителен 5 минут. Если вы не запрашивали код, проигнорируйте письмо.</p>
            </body></html>
            """;

        EmailSendResult result = await _sender.SendAsync(
            toEmail, string.Empty, subject, html, text, transactional: true, ct);
        // Emails are PII (152-ФЗ) — log only a stable SHA-prefix for correlation,
        // mirroring the AuthAuditLog.HashEmail policy.
        if (result.IsSuccess)
            _logger.LogInformation("OTP sent to {EmailHash}", HashEmail(toEmail));
        else
            _logger.LogWarning(
                "Failed to send OTP email to {EmailHash}: {Code} / {Detail}",
                HashEmail(toEmail), result.ErrorCode, result.ErrorDetail);
        return result.IsSuccess;
    }

    public async Task<bool> SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct)
    {
        string subject = "Сброс пароля";
        string text = $"Для сброса пароля перейдите по ссылке:\n\n{resetUrl}\n\nСсылка действительна 1 час. Если вы не запрашивали сброс, проигнорируйте письмо.";
        string safeUrl = WebUtility.HtmlEncode(resetUrl);
        string html = $"""
            <!DOCTYPE html>
            <html><body style="font-family:sans-serif;max-width:600px;margin:0 auto;padding:20px;">
              <h2 style="color:#1a1a1a;">Сброс пароля</h2>
              <p style="color:#4a4a4a;">Нажмите кнопку, чтобы задать новый пароль:</p>
              <p style="text-align:center;margin:24px 0;">
                <a href="{safeUrl}" style="background:#0066cc;color:white;padding:12px 24px;text-decoration:none;border-radius:6px;display:inline-block;">Сбросить пароль</a>
              </p>
              <p style="color:#999;font-size:12px;">Ссылка действительна 1 час. Если вы не запрашивали сброс — проигнорируйте письмо.</p>
            </body></html>
            """;

        EmailSendResult result = await _sender.SendAsync(
            toEmail, string.Empty, subject, html, text, transactional: true, ct);
        if (result.IsSuccess)
            _logger.LogInformation("Password reset email sent to {EmailHash}", HashEmail(toEmail));
        else
            _logger.LogWarning(
                "Failed to send password reset email to {EmailHash}: {Code} / {Detail}",
                HashEmail(toEmail), result.ErrorCode, result.ErrorDetail);
        return result.IsSuccess;
    }

    private static string HashEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "unknown";

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..12];
    }
}
