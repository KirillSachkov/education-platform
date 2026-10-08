using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using TrainerService.Core.Configuration;
using TrainerService.Domain;

namespace TrainerService.Core.Features.Shared;

/// <summary>
///     Per-process burst limiter for inline OPEN_TEXT AI grading. It applies only to the path that is
///     about to call the LLM; deterministic choice/exact answers are not throttled here.
/// </summary>
public sealed class TrainerOpenGradeRateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<Guid, TokenBucketRateLimiter> _limiters = new();
    private readonly IOptions<TrainerAiOptions> _options;

    public TrainerOpenGradeRateLimiter(IOptions<TrainerAiOptions> options)
    {
        _options = options;
    }

    public async Task<UnitResult<Error>> TryAcquireAsync(Guid userId, bool isAdmin, CancellationToken ct)
    {
        if (isAdmin)
            return UnitResult.Success<Error>();

        int limit = _options.Value.OpenGradeRateLimitPerMinute;
        if (limit <= 0)
            return UnitResult.Success<Error>();

        TokenBucketRateLimiter limiter = _limiters.GetOrAdd(userId, _ => CreateLimiter(limit));
        using RateLimitLease lease = await limiter.AcquireAsync(permitCount: 1, ct);
        return lease.IsAcquired
            ? UnitResult.Success<Error>()
            : TrainerServiceErrors.Access.OpenGradeRateLimitExceeded(limit);
    }

    public void Dispose()
    {
        foreach (TokenBucketRateLimiter limiter in _limiters.Values)
            limiter.Dispose();
    }

    private static TokenBucketRateLimiter CreateLimiter(int limit) =>
        new(new TokenBucketRateLimiterOptions
        {
            TokenLimit = limit,
            TokensPerPeriod = limit,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}
