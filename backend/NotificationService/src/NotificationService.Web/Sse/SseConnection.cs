using System.Threading.Channels;

namespace NotificationService.Web.Sse;

/// <summary>
/// Одно активное SSE-соединение. Endpoint-handler регистрирует <see cref="SseConnection"/>
/// в <see cref="SseConnectionHub"/>, после чего другие участники (fan-out consumer, heartbeat service)
/// пишут фреймы в <see cref="Writer"/>. Сам handler читает из <see cref="Reader"/> и пишет в HTTP response.
/// </summary>
public sealed class SseConnection : IAsyncDisposable
{
    /// <summary>
    /// Максимум фреймов в буфере на одно SSE-соединение. Slow / suspended клиент (закрыл крышку
    /// ноутбука, OS держит TCP полу-открытым минутами) не должен бесконечно накапливать broadcast-трафик
    /// — overflow обрезает старые фреймы (<see cref="BoundedChannelFullMode.DropOldest"/>). Клиент
    /// потом сам подтянет пропущенное через EventSource auto-reconnect + GET /notifications inbox
    /// fetch.
    /// </summary>
    private const int MAX_QUEUED_FRAMES = 256;

    private readonly Channel<string> _channel;

    public SseConnection(Guid userId)
    {
        UserId = userId;
        ConnectionId = Guid.CreateVersion7();
        // Bounded: защита от unbounded memory growth на suspended клиентах. На overflow дропаем
        // самые старые фреймы — клиент восстановит state'у через reconnect + inbox fetch.
        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(MAX_QUEUED_FRAMES)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public Guid UserId { get; }
    public Guid ConnectionId { get; }
    public ChannelReader<string> Reader => _channel.Reader;
    public ChannelWriter<string> Writer => _channel.Writer;

    public ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
