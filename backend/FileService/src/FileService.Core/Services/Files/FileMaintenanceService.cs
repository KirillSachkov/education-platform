using Core.Database;
using FileService.Core.Repositories;
using FileService.Domain;
using Microsoft.Extensions.Options;

namespace FileService.Core.Services.Files;

public sealed class FileMaintenanceService
{
    private readonly ILogger<FileMaintenanceService> _logger;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly FileMaintenanceOptions _options;
    private readonly FileServiceMetrics _metrics;

    public FileMaintenanceService(
        ILogger<FileMaintenanceService> logger,
        IMediaAssetRepository assetRepository,
        ITransactionManager transactionManager,
        IOptions<FileMaintenanceOptions> options,
        FileServiceMetrics metrics)
    {
        _logger = logger;
        _assetRepository = assetRepository;
        _transactionManager = transactionManager;
        _options = options.Value;
        _metrics = metrics;
    }

    public async Task<int> CleanupPendingFileUploadsAsync(CancellationToken cancellationToken)
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime cutoff = utcNow.AddMinutes(-_options.PendingUploadTtlMinutes);
        List<MediaAsset> staleAssets = await _assetRepository.GetPendingFileCleanupBatchAsync(
            _options.PendingUploadBatchSize,
            cutoff,
            cancellationToken);

        return await CleanupBatchAsync(
            staleAssets,
            utcNow,
            AssetStaleWorkflowType.PendingFileUpload,
            count => _metrics.RecordPendingFileUploadStuck(count),
            "pending file upload cleanup",
            cancellationToken);
    }

    public async Task<int> CleanupOrphanDraftAssetsAsync(CancellationToken cancellationToken)
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime cutoff = utcNow.AddDays(-_options.OrphanDraftTtlDays);
        List<MediaAsset> staleAssets = await _assetRepository.GetOrphanDraftCleanupBatchAsync(
            _options.OrphanDraftCleanupBatchSize,
            cutoff,
            cancellationToken);

        return await CleanupBatchAsync(
            staleAssets,
            utcNow,
            AssetStaleWorkflowType.DraftFile,
            count => _metrics.RecordStaleDraftFile(count),
            "orphan draft asset cleanup",
            cancellationToken);
    }

    public async Task<int> CleanupStaleDraftFilesAsync(CancellationToken cancellationToken)
    {
        DateTime utcNow = DateTime.UtcNow;
        DateTime cutoff = utcNow.AddHours(-_options.DraftAssetTtlHours);
        List<MediaAsset> staleAssets = await _assetRepository.GetDraftFileCleanupBatchAsync(
            _options.DraftCleanupBatchSize,
            cutoff,
            cancellationToken);

        return await CleanupBatchAsync(
            staleAssets,
            utcNow,
            AssetStaleWorkflowType.DraftFile,
            count => _metrics.RecordStaleDraftFile(count),
            "stale draft file cleanup",
            cancellationToken);
    }

    private async Task<int> CleanupBatchAsync(
        IReadOnlyList<MediaAsset> assets,
        DateTime utcNow,
        AssetStaleWorkflowType workflowType,
        Action<int> metricRecorder,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (assets.Count == 0)
        {
            return 0;
        }

        int mutatedCount = 0;

        foreach (MediaAsset asset in assets)
        {
            if (!asset.IsStale(utcNow, workflowType))
            {
                continue;
            }

            UnitResult<Error> requestDeleteResult = asset.RequestDelete();
            if (requestDeleteResult.IsFailure)
            {
                _logger.LogWarning(
                    "Cleanup skipping asset {AssetId} during {OperationName}: {ErrorType} {ErrorMessage}",
                    asset.Id,
                    operationName,
                    requestDeleteResult.Error.Type,
                    requestDeleteResult.Error.GetMessage());
                continue;
            }

            metricRecorder(1);
            mutatedCount++;
        }

        if (mutatedCount == 0)
        {
            return 0;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to persist {OperationName}: {ErrorType} {ErrorMessage}",
                operationName,
                saveResult.Error.Type,
                saveResult.Error.GetMessage());
            return 0;
        }

        return mutatedCount;
    }
}
