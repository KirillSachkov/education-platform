using FileService.Core;
using FileService.Core.Services.AssetRegistry;
using FileService.Core.Services.Files;
using FileService.Core.Services.Videos;
using Microsoft.Extensions.Options;

namespace FileService.Web.Jobs;

public sealed class FileServiceBackgroundJobs : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FileServiceBackgroundJobs> _logger;
    private readonly FileMaintenanceOptions _fileOptions;
    private readonly VideoMaintenanceOptions _videoOptions;
    private readonly AssetRetentionOptions _retentionOptions;

    public FileServiceBackgroundJobs(
        IServiceScopeFactory scopeFactory,
        IOptions<FileMaintenanceOptions> fileOptions,
        IOptions<VideoMaintenanceOptions> videoOptions,
        IOptions<AssetRetentionOptions> retentionOptions,
        ILogger<FileServiceBackgroundJobs> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _fileOptions = fileOptions.Value;
        _videoOptions = videoOptions.Value;
        _retentionOptions = retentionOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTime nextPendingCleanupAt = DateTime.UtcNow;
        DateTime nextDraftCleanupAt = DateTime.UtcNow;
        DateTime nextOrphanDraftCleanupAt = DateTime.UtcNow;
        DateTime nextVideoReconciliationAt = DateTime.UtcNow;
        DateTime nextRetentionSweepAt = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTime utcNow = DateTime.UtcNow;

            try
            {
                await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                FileMaintenanceService fileMaintenanceService = scope.ServiceProvider.GetRequiredService<FileMaintenanceService>();
                VideoReconciliationService videoReconciliationService = scope.ServiceProvider.GetRequiredService<VideoReconciliationService>();
                AssetRetentionService assetRetentionService = scope.ServiceProvider.GetRequiredService<AssetRetentionService>();

                if (utcNow >= nextPendingCleanupAt)
                {
                    int cleaned = await fileMaintenanceService.CleanupPendingFileUploadsAsync(stoppingToken);
                    if (cleaned > 0)
                    {
                        _logger.LogInformation("Pending file upload cleanup removed {Count} assets", cleaned);
                    }

                    nextPendingCleanupAt = utcNow.AddMinutes(_fileOptions.PendingUploadCleanupIntervalMinutes);
                }

                if (utcNow >= nextDraftCleanupAt)
                {
                    int cleaned = await fileMaintenanceService.CleanupStaleDraftFilesAsync(stoppingToken);
                    if (cleaned > 0)
                    {
                        _logger.LogInformation("Draft file cleanup removed {Count} assets", cleaned);
                    }

                    nextDraftCleanupAt = utcNow.AddMinutes(_fileOptions.DraftCleanupIntervalMinutes);
                }

                if (utcNow >= nextOrphanDraftCleanupAt)
                {
                    int cleaned = await fileMaintenanceService.CleanupOrphanDraftAssetsAsync(stoppingToken);
                    if (cleaned > 0)
                    {
                        _logger.LogInformation("Orphan draft asset cleanup removed {Count} assets", cleaned);
                    }

                    nextOrphanDraftCleanupAt = utcNow.AddMinutes(_fileOptions.OrphanDraftCleanupIntervalMinutes);
                }

                if (utcNow >= nextVideoReconciliationAt)
                {
                    int reconciled = await videoReconciliationService.ReconcileVideosAsync(stoppingToken);
                    if (reconciled > 0)
                    {
                        _logger.LogInformation("Video reconciliation updated {Count} assets", reconciled);
                    }

                    nextVideoReconciliationAt = utcNow.AddMinutes(_videoOptions.VideoReconciliationIntervalMinutes);
                }

                if (utcNow >= nextRetentionSweepAt)
                {
                    int processed = await assetRetentionService.ProcessDeletingAssetsAsync(stoppingToken);
                    int purged = await assetRetentionService.PurgeDeletedAssetsAsync(stoppingToken);

                    if (processed > 0 || purged > 0)
                    {
                        _logger.LogInformation("Retention sweep finalized {Processed} deletes and purged {Purged} tombstones", processed, purged);
                    }

                    nextRetentionSweepAt = utcNow.AddMinutes(_retentionOptions.RetentionSweepIntervalMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FileService background job iteration failed");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
