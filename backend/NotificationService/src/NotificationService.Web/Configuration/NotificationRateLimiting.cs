using System.Security.Claims;
using System.Threading.RateLimiting;
using NotificationService.Core;
using SharedKernel;

namespace NotificationService.Web.Configuration;

/// <summary>
/// Регистрация Rate-limit policies для NotificationService.
/// Партиция всегда по userId (sub claim) → anonymous / service-to-service попадают в отдельные
/// bucket'ы по IP, чтобы не ограничивать всех пользователей одним "anonymous" partition.
///
/// <c>OnRejected</c> возвращает стандартный Envelope с кодом <c>notifications.rate_limit.exceeded</c>
/// — фронт уже умеет это переводить через <c>getErrorMessage</c>.
/// </summary>
public static class NotificationRateLimiting
{
    public static IServiceCollection AddNotificationRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<NotificationRateLimitOptions>()
            .BindConfiguration(NotificationRateLimitOptions.SECTION_NAME)
            .Validate(
                o => o.StreamPermitLimit > 0,
                $"{nameof(NotificationRateLimitOptions.StreamPermitLimit)} must be > 0")
            .Validate(
                o => o.PreferencesUpdatePermitLimit > 0 && o.PreferencesUpdateWindowMinutes > 0,
                "Preferences update limits must be > 0")
            .Validate(
                o => o.BroadcastPermitLimit > 0 && o.BroadcastWindowMinutes > 0,
                "Broadcast limits must be > 0")
            .ValidateOnStart();

        NotificationRateLimitOptions opts =
            configuration.GetSection(NotificationRateLimitOptions.SECTION_NAME)
                .Get<NotificationRateLimitOptions>() ?? new();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                Error error = Error.Failure(
                    "notifications.rate_limit.exceeded",
                    "Слишком много запросов. Попробуйте позже.");
                Envelope<object> envelope = Envelope<object>.Fail(error);
                await context.HttpContext.Response.WriteAsJsonAsync(envelope, cancellationToken);
            };

            // STREAM — concurrency-limiter: держим ≤ N одновременных SSE-connection'ов на user'а.
            // FixedWindow тут не подходит (SSE — long-lived). ConcurrencyLimiter освобождает
            // permit при закрытии соединения → как только user закрывает вкладку, слот свободен.
            options.AddPolicy(
                NotificationRateLimitPolicies.STREAM,
                httpContext =>
                    RateLimitPartition.GetConcurrencyLimiter(
                        PartitionKey(httpContext),
                        _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = opts.StreamPermitLimit,
                            QueueLimit = 0,
                        }));

            // PREFERENCES update — FixedWindow 10/min.
            options.AddPolicy(
                NotificationRateLimitPolicies.PREFERENCES,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = opts.PreferencesUpdatePermitLimit,
                            Window = TimeSpan.FromMinutes(opts.PreferencesUpdateWindowMinutes),
                            QueueLimit = 0,
                        }));

            // BROADCAST — FixedWindow 5/60min (тяжёлая операция fan-out).
            options.AddPolicy(
                NotificationRateLimitPolicies.BROADCAST,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = opts.BroadcastPermitLimit,
                            Window = TimeSpan.FromMinutes(opts.BroadcastWindowMinutes),
                            QueueLimit = 0,
                        }));

            // PUSH_REGISTER — FixedWindow 30/min: антиспам регистрации/отписки устройств (#342).
            // Литеральные значения (не через options) — параметр без потребности в тюнинге.
            options.AddPolicy(
                NotificationRateLimitPolicies.PUSH_REGISTER,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }));

            // OPEN — public click-through performs a notification lookup even for unknown ids.
            options.AddPolicy(
                NotificationRateLimitPolicies.OPEN,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        PartitionKey(httpContext),
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 30,
                            Window = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                        }));
        });

        return services;
    }

    /// <summary>
    /// Партиция по userId (sub / NameIdentifier), fallback на IP. Anonymous SSE → все на одном
    /// "anonymous" partition — это намеренно: anonymous stream и так return'ит 401 (policy
    /// RequireAuthorization), но защищаем от DoS'а на уровне допуска.
    /// </summary>
    private static string PartitionKey(HttpContext httpContext)
    {
        return httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? httpContext.User.FindFirstValue("sub")
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";
    }
}
