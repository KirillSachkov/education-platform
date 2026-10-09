using System.Threading.RateLimiting;

namespace EducationContentService.Web.Configuration;

public static class EducationRateLimiting
{
    public const string ANONYMOUS_READ_POLICY = "anonymous-read";

    public static IServiceCollection AddEducationRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sliding-window per-IP limiter for anonymous/public read endpoints: 60 req/min.
            options.AddPolicy(ANONYMOUS_READ_POLICY, httpContext =>
            {
                string partitionKey = httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
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