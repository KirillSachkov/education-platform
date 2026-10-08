using Microsoft.Extensions.Options;
using SearchService.Core.Database;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using SearchService.Core.Reindex;
using SearchService.Core.Reindex.State;

namespace SearchService.Web.Configuration;

/// <summary>
/// Периодический cron-job: раз в <see cref="SearchReindexReconciliationOptions.Interval"/>
/// публикует <see cref="FullSearchReindexRequested"/>. Защита от дрейфа
/// Typesense ↔ Postgres, если live-event path что-то потерял (рестарт consumer'а
/// в середине, broker hiccup, сетевые потери).
/// </summary>
/// <remarks>
/// Brute-force: всегда полный rebuild через тот же blue/green-путь, что и админская
/// кнопка. Защита от лишних прогонов — <see cref="SearchReindexReconciliationOptions.MinIntervalSinceLast"/>:
/// если предыдущий реиндекс (любой — startup, admin, сам cron) завершился слишком
/// недавно, тик пропускается.
///
/// Отключён по умолчанию (<see cref="SearchReindexReconciliationOptions.Enabled"/> = false) —
/// включается через конфиг на проде.
/// </remarks>
public sealed class SearchReindexReconciliationService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SearchReindexOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SearchReindexReconciliationService> _logger;

    public SearchReindexReconciliationService(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<SearchReindexOptions> options,
        TimeProvider timeProvider,
        ILogger<SearchReindexReconciliationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SearchReindexReconciliationOptions reconciliation = _options.CurrentValue.Reconciliation;

        // Enabled читается ОДИН раз при старте процесса. Hot-reload в `false → true`
        // в проде не подхватится без рестарта. SearchService — single-replica с дешёвым
        // рестартом (alias-swap в Typesense без потери данных), поэтому это
        // приемлемое ограничение. Если понадобится hot-toggle — перенести проверку
        // внутрь while-loop'а.
        if (!reconciliation.Enabled)
        {
            _logger.LogInformation("Search reindex reconciliation disabled; cron service will idle");
            return;
        }

        try
        {
            // jitter перед первым тиком — для multi-replica scenarios. Random.Shared
            // достаточно: jitter не используется в security-context, просто разводим
            // несколько реплик по времени.
#pragma warning disable CA5394 // Do not use insecure randomness — see comment above
            if (reconciliation.StartupJitter > TimeSpan.Zero)
            {
                TimeSpan jitter = TimeSpan.FromMilliseconds(
                    Random.Shared.NextDouble() * reconciliation.StartupJitter.TotalMilliseconds);
                await Task.Delay(jitter, stoppingToken);
            }
#pragma warning restore CA5394

            // первый тик — через Interval после старта (а не сразу), чтобы не пересекаться
            // со startup-реиндексом на свежем деплое
            await Task.Delay(reconciliation.Interval, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EvaluateAndTriggerAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Search reindex reconciliation tick failed; will retry on next interval");
                }

                await Task.Delay(_options.CurrentValue.Reconciliation.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    /// <summary>
    /// Один tick reconciliation: проверяет MinIntervalSinceLast и публикует
    /// <see cref="FullSearchReindexRequested"/>, если можно. Public для интеграционных
    /// тестов; в проде вызывается из <see cref="ExecuteAsync"/> по таймеру.
    /// </summary>
    public async Task EvaluateAndTriggerAsync(CancellationToken cancellationToken)
    {
        SearchReindexReconciliationOptions reconciliation = _options.CurrentValue.Reconciliation;

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ISearchReindexStateRepository repository = scope.ServiceProvider
            .GetRequiredService<ISearchReindexStateRepository>();
        IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        SearchReindexState state = await repository.GetOrInitAsync(cancellationToken);
        DateTime nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        if (state.LastAppliedAtUtc is { } lastAppliedAt)
        {
            TimeSpan sinceLast = nowUtc - lastAppliedAt;
            if (sinceLast < reconciliation.MinIntervalSinceLast)
            {
                _logger.LogInformation(
                    "Search reindex reconciliation tick skipped: previous reindex was {Hours:F1}h ago (min interval {MinHours:F1}h)",
                    sinceLast.TotalHours,
                    reconciliation.MinIntervalSinceLast.TotalHours);
                return;
            }
        }

        Guid requestId = Guid.CreateVersion7();

        await outbox.PublishAsync(
            new FullSearchReindexRequested(requestId, nowUtc),
            cancellationToken);

        _logger.LogInformation(
            "Search reindex reconciliation triggered. RequestId: {RequestId}",
            requestId);
    }
}
