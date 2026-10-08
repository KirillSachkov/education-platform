using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramBotService.Core.Database;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Раз в сутки чистит <c>bot_decisions</c> старше 90 дней. Diagnostic-таблица —
///     долго хранить смысла нет. Между репликами защиты нет (DELETE идемпотентен).
/// </summary>
internal sealed class BotDecisionRetentionService : BackgroundService
{
    private static readonly TimeSpan INITIAL_DELAY = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan INTERVAL = TimeSpan.FromHours(24);
    private static readonly TimeSpan RETENTION = TimeSpan.FromDays(90);
    private const int BATCH_SIZE = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BotDecisionRetentionService> _logger;

    public BotDecisionRetentionService(
        IServiceScopeFactory scopeFactory,
        ILogger<BotDecisionRetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(INITIAL_DELAY, stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BotDecisionRetentionService cleanup failed");
            }

            try { await Task.Delay(INTERVAL, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IBotDecisionRepository repo = scope.ServiceProvider
            .GetRequiredService<IBotDecisionRepository>();

        DateTime cutoff = DateTime.UtcNow - RETENTION;

        int totalDeleted = 0;
        int batch;
        do
        {
            batch = await repo.DeleteOlderThanAsync(cutoff, BATCH_SIZE, ct);
            totalDeleted += batch;
        } while (batch == BATCH_SIZE && !ct.IsCancellationRequested);

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "BotDecision retention deleted {Count} rows older than {Cutoff:O}",
                totalDeleted, cutoff);
        }
    }
}
