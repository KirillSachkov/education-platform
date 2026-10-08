using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Каждые 6 часов проходит по всем ChatBinding'ам и через
///     <see cref="ChatBindingHealthService"/> проверяет права бота. F1/F2/F6 для unhealthy
///     binding'ов всё ещё пытаются исполниться, но падают молча на стороне Telegram API
///     (бот не админ → 403); admin видит флажок «требует внимания» в UI и может починить
///     быстрее, чем ждать пока юзеры начнут жаловаться на «бот не пускает».
///
///     Запускается с задержкой 1 минута после старта сервиса — чтобы не блокировать
///     стартап и дать время инициализироваться внешним зависимостям.
///
///     Между repликами защиты от дублирования НЕТ — операция идемпотентна (UPDATE по PK
///     с теми же значениями), допустимо что обе реплики проверят одни и те же binding'и.
/// </summary>
internal sealed class ChatBindingHealthCheckService : BackgroundService
{
    private static readonly TimeSpan INITIAL_DELAY = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromHours(6);

    // Telegram getChat / getChatMember are bot-API method calls under the 30 req/sec global cap.
    // 100 ms per binding keeps the sweep at ≤10 calls/sec — well under the limit and leaves
    // headroom for live notification delivery happening in parallel.
    private static readonly TimeSpan PER_BINDING_DELAY = TimeSpan.FromMilliseconds(100);
    private const int MAX_BINDINGS_PER_RUN = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatBindingHealthCheckService> _logger;

    public ChatBindingHealthCheckService(
        IServiceScopeFactory scopeFactory,
        ILogger<ChatBindingHealthCheckService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(INITIAL_DELAY, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ChatBindingHealthCheckService run failed");
            }

            try
            {
                await Task.Delay(CHECK_INTERVAL, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IChatBindingRepository bindings = scope.ServiceProvider
            .GetRequiredService<IChatBindingRepository>();
        ChatBindingHealthService health = scope.ServiceProvider
            .GetRequiredService<ChatBindingHealthService>();

        IReadOnlyList<ChatBinding> batch = await bindings.GetHealthCheckBatchAsync(
            MAX_BINDINGS_PER_RUN,
            ct);
        if (batch.Count == 0)
            return;

        int healthy = 0, unhealthy = 0;
        for (int i = 0; i < batch.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            ChatBinding binding = batch[i];
            try
            {
                bool ok = await health.ValidateAsync(binding, ct);
                if (ok) healthy++;
                else unhealthy++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to validate ChatBinding {BindingId}", binding.Id);
            }

            if (i < batch.Count - 1)
            {
                try { await Task.Delay(PER_BINDING_DELAY, ct); }
                catch (OperationCanceledException) { return; }
            }
        }

        _logger.LogInformation(
            "ChatBinding health check finished: {Healthy} healthy, {Unhealthy} unhealthy " +
            "(checked {Checked} oldest bindings)",
            healthy, unhealthy, batch.Count);
    }
}
