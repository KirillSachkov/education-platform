using System.Data.Common;
using Core.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AssignmentReviewService.Core.Maintenance;

/// <summary>
///     Раз в сутки сносит wolverine_dead_letters envelope'ы старше
///     <see cref="DeadLetterCleanupOptions.MaxAge"/>. Без этого таблица растёт
///     бесконечно — нет встроенного TTL'а в Wolverine.
///
///     <para>
///         Issue #328 (ARS hardening). Если понадобится для других сервисов —
///         вынести в <c>Shared/Wolverine.Maintenance</c> как generic helper.
///     </para>
/// </summary>
internal sealed class DeadLetterCleanupBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DeadLetterCleanupOptions> _options;
    private readonly ILogger<DeadLetterCleanupBackgroundService> _logger;

    public DeadLetterCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeadLetterCleanupOptions> options,
        ILogger<DeadLetterCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DeadLetterCleanupOptions opts = _options.Value;
        if (!opts.Enabled)
        {
            _logger.LogInformation("Wolverine dead-letter cleanup disabled via config");
            return;
        }

        // Initial small delay чтобы не дёргать БД сразу при старте — pod ещё
        // прогревается, миграции могут идти.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int deleted = await RunCleanupAsync(opts.MaxAge, stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Pruned {Deleted} dead-letter envelopes older than {MaxAge}",
                        deleted, opts.MaxAge);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (NpgsqlException ex)
            {
                _logger.LogWarning(ex, "Dead-letter cleanup failed — will retry on next interval");
            }

            try
            {
                await Task.Delay(opts.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> RunCleanupAsync(TimeSpan maxAge, CancellationToken ct)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        DbConnection connection = transactions.GetDbConnection();

        DateTimeOffset cutoff = DateTimeOffset.UtcNow.Subtract(maxAge);

        // Используем sent_at (когда envelope попал в dead-letter). Если null —
        // тоже сносим, такие записи устаревшие по факту существования рядом
        // с пометкой времени.
        const string sql = """
            DELETE FROM assignment_review.wolverine_dead_letters
            WHERE sent_at IS NULL OR sent_at < @Cutoff
            """;

        return await connection.ExecuteAsync(
            new CommandDefinition(sql, new { Cutoff = cutoff }, cancellationToken: ct));
    }
}
