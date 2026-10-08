using System.Collections.Concurrent;
using AuthService.Core.Services;

namespace AuthService.IntegrationTests.Infrastructure;

public sealed class FakeEmailSender : IAuthEmailSender
{
    public ConcurrentBag<(string Email, string Code)> SentCodes { get; } = [];
    public ConcurrentBag<(string Email, string Url)> SentResetLinks { get; } = [];

    public Task<bool> SendOtpAsync(string toEmail, string code, CancellationToken ct)
    {
        SentCodes.Add((toEmail, code));
        return Task.FromResult(true);
    }

    public Task<bool> SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct)
    {
        SentResetLinks.Add((toEmail, resetUrl));
        return Task.FromResult(true);
    }

    public void Clear()
    {
        SentCodes.Clear();
        SentResetLinks.Clear();
    }
}
