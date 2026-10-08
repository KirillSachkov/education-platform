using System.Collections.Concurrent;
using NotificationService.Core.Sse;

namespace NotificationService.Web.Sse;

/// <summary>
/// In-memory реестр активных SSE-соединений per-replica. Для multi-replica scale-out
/// работает в связке с <c>SseRedisSubscriberService</c> (см. Phase C.4 SSE fanout).
/// </summary>
public sealed class SseConnectionHub : ISseConnectionHub
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, SseConnection>> _byUser = new();

    public void Register(SseConnection connection)
    {
        ConcurrentDictionary<Guid, SseConnection> inner = _byUser.GetOrAdd(
            connection.UserId,
            static _ => new ConcurrentDictionary<Guid, SseConnection>());
        inner.TryAdd(connection.ConnectionId, connection);
    }

    public void Unregister(SseConnection connection)
    {
        if (!_byUser.TryGetValue(connection.UserId, out ConcurrentDictionary<Guid, SseConnection>? inner))
            return;

        inner.TryRemove(connection.ConnectionId, out _);

        // Снимаем внешний ключ, если внутренний словарь опустел. Небольшая гонка допустима —
        // при параллельном Register заново создаст пустой словарь и доавит соединение.
        if (inner.IsEmpty)
            _byUser.TryRemove(connection.UserId, out _);
    }

    public Task PushAsync(Guid userId, string eventName, string json, CancellationToken ct = default)
    {
        if (!_byUser.TryGetValue(userId, out ConcurrentDictionary<Guid, SseConnection>? inner))
            return Task.CompletedTask;

        string frame = FormatFrame(eventName, json);
        foreach (SseConnection connection in inner.Values)
        {
            // TryWrite — неблокирующий; если channel уже Complete'нутый (клиент отключился) — игнорируем.
            connection.Writer.TryWrite(frame);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Итерация по всем активным соединениям (используется heartbeat service'ом).
    /// Возвращает snapshot — дальнейшие изменения hub'а в нём не отражаются.
    /// </summary>
    public IReadOnlyList<SseConnection> Snapshot()
    {
        List<SseConnection> result = [];
        foreach (ConcurrentDictionary<Guid, SseConnection> inner in _byUser.Values)
        {
            foreach (SseConnection connection in inner.Values)
                result.Add(connection);
        }
        return result;
    }

    private static string FormatFrame(string eventName, string data) =>
        $"event: {eventName}\ndata: {data}\n\n";
}
