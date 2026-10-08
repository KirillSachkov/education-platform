using System.Security.Claims;
using System.Threading.RateLimiting;

namespace SearchService.Web.Configuration;

public static class SearchRateLimiting
{
    /// <summary>
    /// Политика для публичного эндпоинта /search — защищает Typesense от fan-out
    /// на typing-автокомплит. Аноним: 30 req / 10s per IP. Аутентифицированный:
    /// 60 req / 10s per user (NameIdentifier/sub). Фикс-окно, без очереди (fail-fast 429).
    /// </summary>
    public const string SEARCH_PUBLIC_POLICY = "search-public";

    public static IServiceCollection AddSearchRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(SEARCH_PUBLIC_POLICY, httpContext =>
            {
                string? sub = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? httpContext.User.FindFirstValue("sub");

                if (!string.IsNullOrEmpty(sub))
                {
                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"user:{sub}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 60,
                            Window = TimeSpan.FromSeconds(10),
                            QueueLimit = 0,
                        });
                }

                string partitionKey = httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(
                    $"ip:{partitionKey}",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromSeconds(10),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }
}
