using AuthService.Core.Services;

namespace AuthService.IntegrationTests.Infrastructure;

public sealed class FakeOtpAttemptLimiter : OtpAttemptLimiter
{
    public FakeOtpAttemptLimiter() : base(null!, null!)
    {
    }

    public override Task<bool> TryAttemptAsync(string email) => Task.FromResult(true);
}
