using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Options;
using SharedKernel;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.ContentDrafts;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Web.Jobs;

/// <summary>
///     Periodically sweeps timecode_generation_jobs and content_generation_jobs rows
///     stuck in <c>PROCESSING</c> beyond the configured threshold and marks them
///     <c>FAILED</c>. Without this:
///     <list type="bullet">
///         <item>OOM-killed pod mid-STT leaves a row in PROCESSING forever.</item>
///         <item>Partial unique index <c>ux_timecode_jobs_video_active</c> blocks
///             any new job for the same videoId — author cannot retry, sees no
///             error, no recovery path.</item>
///         <item>Same applies to content_generation_jobs and its
///             <c>(video_asset_id, material_id)</c> active index.</item>
///     </list>
///     Threshold: <c>StuckThreshold</c> (default 45 min since the last stage update).
///     Sweep period: <c>SweepInterval</c> (default 5 min).
/// </summary>
public sealed class StuckJobSweeper : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IOptionsMonitor<StuckJobSweeperOptions> _options;
    private readonly ILogger<StuckJobSweeper> _logger;

    public StuckJobSweeper(
        IServiceProvider services,
        IOptionsMonitor<StuckJobSweeperOptions> options,
        ILogger<StuckJobSweeper> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StuckJobSweeper iteration failed; will retry next tick");
            }

            try
            {
                await Task.Delay(_options.CurrentValue.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public async Task<int> SweepOnceAsync(CancellationToken cancellationToken)
    {
        StuckJobSweeperOptions opts = _options.CurrentValue;
        DateTime cutoff = DateTime.UtcNow - opts.StuckThreshold;
        int batchSize = Math.Max(1, opts.BatchSize);

        await using AsyncServiceScope scope = _services.CreateAsyncScope();
        ITimecodeGenerationJobRepository timecodeJobs =
            scope.ServiceProvider.GetRequiredService<ITimecodeGenerationJobRepository>();
        IContentGenerationJobRepository contentJobs =
            scope.ServiceProvider.GetRequiredService<IContentGenerationJobRepository>();
        ITransactionManager transactions =
            scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        IReadOnlyList<TimecodeGenerationJob> stuckTimecodes =
            await timecodeJobs.GetStuckJobsAsync(cutoff, batchSize, cancellationToken);
        IReadOnlyList<ContentGenerationJob> stuckContent =
            await contentJobs.GetStuckJobsAsync(cutoff, batchSize, cancellationToken);

        if (stuckTimecodes.Count == 0 && stuckContent.Count == 0)
            return 0;

        Error stuckError = Error.Failure(
            "job.stuck",
            "Обработка прервана — pod был перезапущен. Запустите генерацию повторно.");

        foreach (TimecodeGenerationJob job in stuckTimecodes)
        {
            job.MarkFailed(stuckError);
        }

        foreach (ContentGenerationJob job in stuckContent)
        {
            job.MarkFailed(stuckError);
        }

        UnitResult<Error> save = await transactions.SaveChangesAsync(cancellationToken);
        if (save.IsFailure)
        {
            _logger.LogError(
                "StuckJobSweeper failed to persist {TimecodeCount} timecode + {ContentCount} content failures: {Error}",
                stuckTimecodes.Count, stuckContent.Count, save.Error.Type);
            return 0;
        }

        int total = stuckTimecodes.Count + stuckContent.Count;
        _logger.LogWarning(
            "StuckJobSweeper marked {Total} stuck jobs as FAILED " +
            "(timecode={TimecodeCount}, content={ContentCount}, cutoff={Cutoff:O})",
            total, stuckTimecodes.Count, stuckContent.Count, cutoff);
        return total;
    }
}

public sealed class StuckJobSweeperOptions
{
    public const string SECTION_NAME = "StuckJobSweeper";

    /// <summary>
    ///     Job без обновления progress/stage дольше этого порога считается зомби.
    ///     Порог 45 минут оставляет запас относительно 30-минутного FFmpeg timeout.
    /// </summary>
    public TimeSpan StuckThreshold { get; set; } = TimeSpan.FromMinutes(45);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);

    public int BatchSize { get; set; } = 50;
}
