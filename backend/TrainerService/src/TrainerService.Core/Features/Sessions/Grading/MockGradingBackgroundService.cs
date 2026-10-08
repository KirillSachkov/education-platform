using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TrainerService.Core.Database;

namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     Drains <see cref="MockGradingQueue"/> and grades each completed mock session via a scoped
///     <see cref="MockAnswerGradingService"/> (#585). On startup it sweeps the DB for sessions stuck
///     in PENDING/GRADING (a restart mid-grade, or a Complete that enqueued just before shutdown)
///     and re-enqueues them — so AI grading survives a process restart without a durable broker.
/// </summary>
public sealed class MockGradingBackgroundService : BackgroundService
{
    private const int RECOVERY_BATCH_SIZE = 1_024;
    private static readonly TimeSpan RecoveryInterval = TimeSpan.FromMinutes(1);

    private readonly MockGradingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MockGradingBackgroundService> _logger;

    public MockGradingBackgroundService(
        MockGradingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<MockGradingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(DrainQueueAsync(stoppingToken), RunRecoveryLoopAsync(stoppingToken));

    private async Task DrainQueueAsync(CancellationToken stoppingToken)
    {
        await foreach (Guid sessionId in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            _queue.MarkDequeued(sessionId);
            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                MockAnswerGradingService grader =
                    scope.ServiceProvider.GetRequiredService<MockAnswerGradingService>();
                await grader.GradeSessionAsync(sessionId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // background loop must not die on one session's failure
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // The grading service already persists a FAILED status on unhandled errors; this is
                // the last-resort guard so that one bad session cannot kill the whole drain loop.
                _logger.LogError(ex, "Mock AI grading failed for session {SessionId}.", sessionId);
            }
        }
    }

    private async Task RunRecoveryLoopAsync(CancellationToken ct)
    {
        await RecoverPendingSessionsAsync(ct);

        using var timer = new PeriodicTimer(RecoveryInterval);
        while (await timer.WaitForNextTickAsync(ct))
            await RecoverPendingSessionsAsync(ct);
    }

    /// <summary>
    ///     Startup recovery: re-enqueue sessions left in PENDING/GRADING by a previous process
    ///     (crash / redeploy mid-grade). Best-effort — a failure here must not stop the host.
    /// </summary>
    private async Task RecoverPendingSessionsAsync(CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            var sessions = scope.ServiceProvider.GetRequiredService<ITrainingSessionsRepository>();

            IReadOnlyList<Guid> stuck = await sessions.GetIdsAwaitingGradingAsync(RECOVERY_BATCH_SIZE, ct);

            foreach (Guid sessionId in stuck)
                _queue.Enqueue(sessionId);

            if (stuck.Count > 0)
                _logger.LogInformation("Re-enqueued {Count} mock session(s) for AI grading on startup.", stuck.Count);
        }
#pragma warning disable CA1031 // startup recovery is best-effort — never block the host
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Mock AI grading startup recovery sweep failed.");
        }
    }
}
