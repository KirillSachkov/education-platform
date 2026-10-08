using Microsoft.Extensions.Logging;

namespace NotificationService.Core.Sse;

/// <summary>
/// Заглушка SSE hub'а. Реальная реализация появляется в Phase 1.6 и переопределяет эту регистрацию.
/// </summary>
public sealed class NullSseConnectionHub : ISseConnectionHub
{
    private readonly ILogger<NullSseConnectionHub> _logger;

    public NullSseConnectionHub(ILogger<NullSseConnectionHub> logger) => _logger = logger;

    public Task PushAsync(Guid userId, string eventName, string json, CancellationToken ct = default)
    {
        _logger.LogDebug("SSE push skipped (null hub): user={UserId} event={Event}", userId, eventName);
        return Task.CompletedTask;
    }
}
