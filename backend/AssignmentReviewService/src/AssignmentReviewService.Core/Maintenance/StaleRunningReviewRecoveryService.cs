using System.Data.Common;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Diagnostics;
using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using Core.Database;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AssignmentReviewService.Core.Maintenance;

/// <summary>
///     Recovers AI reviews that lost their Wolverine run command or were left RUNNING
///     by a crashed worker. Startup mode resets every RUNNING review because no old
///     in-process handler can still be alive after process restart. Periodic mode is
///     age-gated and only touches stale RUNNING/QUEUED rows.
/// </summary>
public sealed class StaleRunningReviewRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly AssignmentReviewMetrics _metrics;
    private readonly ILogger<StaleRunningReviewRecoveryService> _logger;

    public StaleRunningReviewRecoveryService(
        IServiceScopeFactory scopeFactory,
        IOptions<AssignmentReviewAiOptions> options,
        AssignmentReviewMetrics metrics,
        ILogger<StaleRunningReviewRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    public Task<int> RecoverStartupAsync(CancellationToken ct) =>
        RecoverAsync(includeAllRunning: true, ct);

    public Task<int> RecoverStaleAsync(CancellationToken ct) =>
        RecoverAsync(includeAllRunning: false, ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverStartupAsync(stoppingToken);

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(30,
            _options.Value.Limits.StaleRecoveryIntervalSeconds));
        using PeriodicTimer timer = new(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RecoverStaleAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task<int> RecoverAsync(bool includeAllRunning, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ITransactionManager transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
            IOutboxService outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
            DbConnection connection = transactions.GetDbConnection();

            // Safety floor: the stale threshold MUST comfortably exceed one LLM call
            // (reviewer TimeoutSeconds). A single-batch review has no per-batch heartbeat —
            // if StaleReviewMaxAgeMinutes were configured below the call timeout, a single
            // legit call would be requeued mid-flight and starve. Clamp so a too-low config
            // override can't resurrect that bug (#690).
            int timeoutMinutesFloor = (_options.Value.Reviewer.TimeoutSeconds / 60) + 2;
            int maxAgeMinutes = Math.Max(
                _options.Value.Limits.StaleReviewMaxAgeMinutes,
                Math.Max(5, timeoutMinutesFloor));
            TimeSpan maxAge = TimeSpan.FromMinutes(maxAgeMinutes);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset staleBefore = now - maxAge;

            // Periodic: RUNNING-ревью считается зависшим по GREATEST(updated_at, heartbeat_at) —
            // т.е. прогрессирующее длинное multi-batch ревью (которое heart-beat'ит после
            // каждого batch'а, #690) не попадает под порог и не starve'ится. GREATEST игнорит
            // NULL heartbeat_at → fallback на updated_at для ревью без heartbeat'а.
            // Startup: сбрасываем ВСЕ RUNNING — после рестарта живых in-process worker'ов нет.
            string runningPredicate = includeAllRunning
                ? "status = 'RUNNING'"
                // GREATEST ignores NULL in Postgres → falls back to updated_at when heartbeat_at IS NULL.
                : "status = 'RUNNING' AND GREATEST(updated_at, heartbeat_at) < @StaleBefore";

            string sql = $"""
                UPDATE assignment_review.ai_reviews
                SET status = 'QUEUED', updated_at = @Now
                WHERE ({runningPredicate})
                   OR (status = 'QUEUED' AND updated_at < @StaleBefore)
                RETURNING id
                """;

            IEnumerable<Guid> recoveredIds = await connection.QueryAsync<Guid>(new CommandDefinition(
                sql,
                new { Now = now, StaleBefore = staleBefore },
                cancellationToken: ct));
            Guid[] recovered = [.. recoveredIds];

            // #690 — gauge зависших ревью. Только periodic-проход: startup сбрасывает ВСЕ
            // RUNNING (это restart-recovery, не «застряло»), он бы зашумил метрику. Пишем и
            // 0 — чтобы gauge спадал, когда застрявших не осталось.
            if (!includeAllRunning)
                _metrics.SetStuckReviewCount(recovered.Length);

            foreach (Guid id in recovered)
            {
                await outbox.PublishAsync(new RunAiReviewRequested(
                    id,
                    ModelOverride: null,
                    AllowOversizedDiff: false,
                    ForceFresh: false));
            }

            if (recovered.Length > 0)
            {
                UnitResult<Error> save = await transactions.SaveChangesAsync(ct);
                if (save.IsFailure)
                    return 0;

                _logger.LogWarning(
                    "StaleRunningReviewRecovery: requeued {Count} stale AI review(s). IDs: {Ids}. includeAllRunning={IncludeAllRunning}",
                    recovered.Length,
                    string.Join(", ", recovered),
                    includeAllRunning);
            }

            return recovered.Length;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(
                ex,
                "StaleRunningReviewRecovery: failed to recover stale AI reviews.");
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "StaleRunningReviewRecovery: unexpected failure while recovering stale AI reviews.");
            return 0;
        }
    }
}
