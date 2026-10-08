using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationService.Core;

namespace NotificationService.Infrastructure.Postgres.Retention;

/// <summary>
/// Фоновый сервис: раз в <see cref="NotificationRetentionOptions.IntervalHours"/> часов удаляет
/// записи из <c>notifications</c> старше <see cref="NotificationRetentionOptions.DefaultDays"/>.
///
/// <para>
/// Особенности реализации:
/// <list type="bullet">
///   <item>Удаление батчами по <see cref="NotificationRetentionOptions.BatchSize"/> строк
///     (через <c>DELETE ... WHERE id IN (SELECT id FROM … LIMIT @n)</c>) — не держим долгий
///     row-lock на большой таблице.</item>
///   <item>Каскад на <c>notification_deliveries</c> выполняется автоматически за счёт FK-cascade
///     (см. <c>NotificationDeliveryConfiguration</c>), отдельный DELETE не нужен.</item>
///   <item>Resilient: исключение в одной итерации логируется, следующий цикл проходит штатно.</item>
///   <item>Scoped DI: создаём scope на каждый проход, чтобы не держать DbContext вечно.</item>
/// </list>
/// </para>
///
/// <para>
/// Subscriptions и user-preferences НЕ удаляются — они персистентные per-user, не растут линейно
/// с трафиком уведомлений.
/// </para>
/// </summary>
public sealed class NotificationRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<NotificationOptions> _options;
    private readonly ILogger<NotificationRetentionService> _logger;

    public NotificationRetentionService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<NotificationOptions> options,
        ILogger<NotificationRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        NotificationRetentionOptions cfg = _options.CurrentValue.Retention;

        if (!cfg.Enabled)
        {
            _logger.LogInformation("Notification retention disabled; background service exiting.");
            return;
        }

        // Отсрочка первого запуска, чтобы не конкурировать с migrate + startup.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(cfg.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromHours(cfg.IntervalHours));

        // Первый прогон сразу (после initial delay), затем по таймеру.
        await RunCleanupCycleAsync(stoppingToken);

        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    return;

                await RunCleanupCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // Один цикл упал — логируем и ждём следующий тик. Не убиваем сервис.
                _logger.LogError(ex, "Notification retention cycle failed; continuing on next interval");
            }
        }
    }

    private async Task RunCleanupCycleAsync(CancellationToken ct)
    {
        NotificationRetentionOptions cfg = _options.CurrentValue.Retention;

        DateTime cutoff = DateTime.UtcNow.AddDays(-cfg.DefaultDays);
        int batchSize = Math.Max(100, cfg.BatchSize);
        int totalDeleted = 0;

        // Цикл пока DELETE возвращает > 0. Защищаем от бесконечного цикла жёстким лимитом
        // итераций: для 10M батчей по 5000 = 50M записей, достаточно для любой реальной нагрузки.
        // Scope пересоздаётся каждые N батчей чтобы возвращать pooled connection в пул и не
        // удерживать его всё многоминутное окно (issue #230, PROD-3).
        const int maxIterations = 10_000;
        const int scopeRefreshEvery = 20;

        AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        try
        {
            NotificationDbContext dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();

            for (int i = 0; i < maxIterations; i++)
            {
                ct.ThrowIfCancellationRequested();

                int deleted = await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                     DELETE FROM notifications.notifications
                     WHERE id IN (
                         SELECT id FROM notifications.notifications
                         WHERE created_at < {cutoff}
                         LIMIT {batchSize}
                     )
                     """,
                    ct);

                totalDeleted += deleted;
                if (deleted < batchSize)
                    break; // последний batch — закончили.

                if ((i + 1) % scopeRefreshEvery == 0)
                {
                    await scope.DisposeAsync();
                    scope = _scopeFactory.CreateAsyncScope();
                    dbContext = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                }
            }
        }
        finally
        {
            await scope.DisposeAsync();
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Notification retention: deleted {Count} rows older than {Cutoff:u} (threshold {Days}d)",
                totalDeleted, cutoff, cfg.DefaultDays);
        }
        else
        {
            _logger.LogDebug(
                "Notification retention: nothing to delete (cutoff {Cutoff:u})", cutoff);
        }
    }
}
