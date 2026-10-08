namespace AuthService.Core.Services;

public interface IAuthEmailSender
{
    Task<bool> SendOtpAsync(string toEmail, string code, CancellationToken ct);
    Task<bool> SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct);
}
