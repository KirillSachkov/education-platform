using System.Security.Claims;
using System.Threading.RateLimiting;

namespace TelegramBotService.Web.Configuration;

/// <summary>
///     Регистрация rate-limit policies для пользовательских HTTP-эндпоинтов TelegramBotService.
///     <c>PlatformBootstrap.AddPlatformDefaults</c> добавляет <c>AddRateLimiter(_ => { })</c>
///     с пустым контейнером — конкретные policies нужно регистрировать локально в каждом сервисе.
/// </summary>
public static class TelegramRateLimiting
{
    public static IServiceCollection AddTelegramRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Resync invites — authenticated user only. 5 per minute, partitioned per user
            // (не на IP — чтобы один пользователь не выбил лимит у соседа за NAT). Зеркалит
            // policy "telegram-link" из AuthService.
            options.AddPolicy("telegram-link", httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(GetUserPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    private static string GetUserPartitionKey(HttpContext httpContext)
    {
        // Authenticated → partition по user id. OpenIddict mappит JWT-claim sub в
        // ClaimTypes.NameIdentifier; зеркалим логику UserScopedDataMiddleware.
        // Anonymous (не должно быть на этом эндпоинте, но на всякий случай) → IP.
        string? sub = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? httpContext.User?.FindFirstValue("sub");
        if (!string.IsNullOrEmpty(sub))
            return $"user:{sub}";

        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}
