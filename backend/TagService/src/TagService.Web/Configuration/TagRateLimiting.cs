using System.Threading.RateLimiting;
using TagService.Core;

namespace TagService.Web.Configuration;

public static class TagRateLimiting
{
    public static IServiceCollection AddTagRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sliding-window per-IP limiter for anonymous read endpoints: 60 requests per minute.
            options.AddPolicy(Constants.ANONYMOUS_READ_RATE_LIMIT_POLICY, httpContext =>
            {
                string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
