namespace NotificationService.Web.Sse;

/// <summary>
/// Раз в <see cref="HEARTBEAT_INTERVAL_SECONDS"/> секунд пушит keep-alive в каждое открытое соединение.
/// SSE-совместимый комментарий вида <c>: ping\n\n</c> не триггерит обработчик событий на клиенте,
/// но удерживает соединение от закрытия proxy/LB при долгом отсутствии событий.
///
/// Resilient: исключение в одной итерации логируется, но цикл продолжается. Иначе один битый
/// connection или transient ошибка убивали весь сервис, и новые SSE-соединения не получали ping'а
/// (proxy_read_timeout закрывал их через минуту).
/// </summary>
public sealed class SseHeartbeatService : BackgroundService
{
    private const int HEARTBEAT_INTERVAL_SECONDS = 30;
    private const string HEARTBEAT_FRAME = ": ping\n\n";

    private readonly SseConnectionHub _hub;
    private readonly ILogger<SseHeartbeatService> _logger;

    public SseHeartbeatService(SseConnectionHub hub, ILogger<SseHeartbeatService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(HEARTBEAT_INTERVAL_SECONDS));

        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;

                IReadOnlyList<SseConnection> snapshot = _hub.Snapshot();
                foreach (SseConnection connection in snapshot)
                {
                    // TryWrite — non-blocking, не падает на битом connection.
                    connection.Writer.TryWrite(HEARTBEAT_FRAME);
                }
            }
            catch (OperationCanceledException)
            {
                // graceful shutdown
                return;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Один тик упал — лог + следующий тик. Не убиваем сервис, иначе новые
                // SSE-connections перестанут получать heartbeat.
                _logger.LogError(ex, "SSE heartbeat tick failed; continuing on next interval");
            }
        }
    }
}
