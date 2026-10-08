using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TelegramBotService.Web.Configuration;

/// <summary>
///     Logs <see cref="LogLevel.Critical"/> on host start when the in-memory idempotency
///     store is wired in a Production environment. The in-memory store cannot dedupe across
///     replicas, so a missing <c>ConnectionStrings:Redis</c> in prod silently risks duplicate
///     Telegram deliveries. Registered only when the fallback path is taken (no Redis).
/// </summary>
internal sealed class RedisIdempotencyGuardHostedService : IHostedService
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RedisIdempotencyGuardHostedService> _logger;

    public RedisIdempotencyGuardHostedService(
        IHostEnvironment environment,
        ILogger<RedisIdempotencyGuardHostedService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            const string message =
                "Redis is required in Production for Telegram delivery and welcome idempotency. " +
                "Set ConnectionStrings:Redis.";
            _logger.LogCritical(message);
            throw new InvalidOperationException(message);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
