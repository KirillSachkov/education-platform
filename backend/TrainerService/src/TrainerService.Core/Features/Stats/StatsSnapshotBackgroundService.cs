using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TrainerService.Core.Database;

namespace TrainerService.Core.Features.Stats;

/// <summary>
///     Daily stat-snapshot job (#681 T1). Runs one snapshot on startup, then once per <see cref="Interval"/>
///     via a <see cref="PeriodicTimer"/> (mirrors <c>MockGradingBackgroundService</c>). Each tick recomputes
///     and (idempotently) rewrites today's snapshot rows. The interval is a code default — there is NO
///     appsettings knob for it (another subtask owns appsettings). Work runs in its own DI scope and every
///     non-cancellation exception is swallowed+logged so one bad tick can't kill the host.
/// </summary>
public sealed class StatsSnapshotBackgroundService : BackgroundService
{
    /// <summary>Snapshot cadence. Daily: snapshots are a once-a-day rollup of the previous day's activity.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StatsSnapshotBackgroundService> _logger;

    public StatsSnapshotBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<StatsSnapshotBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One snapshot immediately on startup, then on every tick.
        await RunTickAsync(stoppingToken);

        using PeriodicTimer timer = new(Interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunTickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — normal exit.
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IStatSnapshotRepository>();

            DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
            StatSnapshotRunResult result = await repository.WriteSnapshotsAsync(today, ct);

            _logger.LogInformation(
                "Trainer stat snapshot for {Date}: {Daily} daily, {Mastery} mastery, {Question} question row(s).",
                today, result.DailyRows, result.TopicMasteryRows, result.QuestionAccuracyRows);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // bubble cancellation to the timer loop so the host can shut down cleanly
        }
#pragma warning disable CA1031 // background loop must not die on one tick's failure
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Trainer stat snapshot tick failed.");
        }
    }
}
