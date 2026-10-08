namespace NotificationService.Core.Sse;

/// <summary>
/// Fallback-реализация. Используется в single-replica конфигурации или в тестах —
/// <see cref="IsEnabled"/> = false → <see cref="ISseConnectionHub.PushAsync"/> работает напрямую.
/// </summary>
public sealed class NullSseRedisPublisher : ISseRedisPublisher
{
    public bool IsEnabled => false;

    public Task PublishAsync(Guid userId, string eventName, string json, CancellationToken ct = default)
        => Task.CompletedTask;
}
