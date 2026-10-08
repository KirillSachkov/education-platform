using Microsoft.Extensions.Options;
using TrainerService.Core.Configuration;
using TrainerService.Core.Features.Shared;

namespace TrainerService.IntegrationTests.Features.AiUsageQuota;

public sealed class OpenGradeRateLimiterTests
{
    [Fact]
    public async Task TryAcquire_blocks_user_after_per_minute_limit_but_admin_bypasses()
    {
        using var limiter = new TrainerOpenGradeRateLimiter(Options.Create(new TrainerAiOptions
        {
            OpenGradeRateLimitPerMinute = 2,
        }));
        Guid userId = Guid.NewGuid();

        Assert.True((await limiter.TryAcquireAsync(userId, isAdmin: false, CancellationToken.None)).IsSuccess);
        Assert.True((await limiter.TryAcquireAsync(userId, isAdmin: false, CancellationToken.None)).IsSuccess);

        UnitResult<Error> blocked = await limiter.TryAcquireAsync(userId, isAdmin: false, CancellationToken.None);
        Assert.True(blocked.IsFailure);
        Assert.Equal("trainer.open_grade.rate_limited", blocked.Error.Messages.Single().Code);

        Assert.True((await limiter.TryAcquireAsync(userId, isAdmin: true, CancellationToken.None)).IsSuccess);
    }
}
