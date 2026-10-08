using Core.Database;
using FileService.Core.FilesStorage;
using FileService.Core.Repositories;
using FileService.Domain;
using Microsoft.Extensions.Options;

namespace FileService.Core.Services.AssetRegistry;

/// <summary>
///     Phase 2 of the two-phase delete pattern. <see cref="DeleteFile"/> /
///     <see cref="DeleteVideo"/> only transition an asset to DELETING and publish
///     <c>FileAssetDeleted</c>. This service periodically picks up DELETING assets,
///     performs the physical provider delete (S3 / Kinescope) and transitions the
///     asset to DELETED. Failures leave the asset in DELETING and it is retried on
///     the next sweep. A later pass purges DELETED file tombstones after
///     <see cref="AssetRetentionOptions.TombstoneRetentionHours"/>. Video tombstones
///     remain as durable ownership records while the provider object is retained.
/// </summary>
public sealed class AssetRetentionService
{
    private readonly ILogger<AssetRetentionService> _logger;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IFileStorageRefRepository _fileStorageRefRepository;
    private readonly IObjectStorageProvider _objectStorageProvider;
    private readonly ITransactionManager _transactionManager;
    private readonly AssetRetentionOptions _options;
    private readonly FileServiceMetrics _metrics;

    public AssetRetentionService(
        ILogger<AssetRetentionService> logger,
        IMediaAssetRepository assetRepository,
        IFileStorageRefRepository fileStorageRefRepository,
        IObjectStorageProvider objectStorageProvider,
        ITransactionManager transactionManager,
        IOptions<AssetRetentionOptions> options,
        FileServiceMetrics metrics)
    {
        _logger = logger;
        _assetRepository = assetRepository;
        _fileStorageRefRepository = fileStorageRefRepository;
        _objectStorageProvider = objectStorageProvider;
        _transactionManager = transactionManager;
        _options = options.Value;
        _metrics = metrics;
    }

    /// <summary>
    ///     Picks up assets in DELETING, performs the provider delete, and transitions
    ///     successful ones to DELETED. A failed provider delete leaves the asset in
    ///     DELETING — the next sweep retries it.
    /// </summary>
    public async Task<int> ProcessDeletingAssetsAsync(CancellationToken cancellationToken)
    {
        // Process any DELETING asset — cutoff = utcNow means we don't artificially
        // delay the first attempt. Failures implicitly retry on the next sweep
        // (natural cadence is RetentionSweepIntervalMinutes).
        List<MediaAsset> assets = await _assetRepository.GetDeletingRetentionBatchAsync(
            _options.DeleteRetryBatchSize,
            DateTime.UtcNow,
            cancellationToken);

        if (assets.Count == 0)
        {
            return 0;
        }

        Guid[] assetIds = assets.Select(a => a.Id).ToArray();
        IReadOnlyDictionary<Guid, FileStorageRef> storageRefs =
            await _fileStorageRefRepository.GetByAssetIdsAsync(assetIds, cancellationToken);

        int mutatedCount = 0;

        foreach (MediaAsset asset in assets)
        {
            _metrics.RecordDeleteRetryAttempt();

            UnitResult<Error> providerDeleteResult = await DeleteFromProviderAsync(
                asset, storageRefs, cancellationToken);
            if (providerDeleteResult.IsFailure)
            {
                _logger.LogWarning(
                    "ProcessDeletingAssetsAsync: provider delete failed for asset {AssetId}: {ErrorType}",
                    asset.Id, providerDeleteResult.Error.Type);
                continue;
            }

            UnitResult<Error> markResult = asset.MarkDeleted();
            if (markResult.IsFailure)
            {
                _logger.LogWarning(
                    "ProcessDeletingAssetsAsync: MarkDeleted failed for asset {AssetId}: {ErrorType}",
                    asset.Id, markResult.Error.Type);
                continue;
            }

            mutatedCount++;
        }

        if (mutatedCount == 0)
        {
            return 0;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError("Failed to persist delete sweep batch: {ErrorType}", saveResult.Error.Type);
            return 0;
        }

        return mutatedCount;
    }

    public async Task<int> PurgeDeletedAssetsAsync(CancellationToken cancellationToken)
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-_options.TombstoneRetentionHours);
        List<MediaAsset> assets = await _assetRepository.GetDeletedPurgeBatchAsync(
            _options.PurgeBatchSize,
            cutoff,
            cancellationToken);

        if (assets.Count == 0)
        {
            return 0;
        }

        foreach (MediaAsset asset in assets)
        {
            await _assetRepository.DeleteAsync(asset, cancellationToken);
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError("Failed to purge deleted assets: {ErrorType}", saveResult.Error.Type);
            return 0;
        }

        return assets.Count;
    }

    private async Task<UnitResult<Error>> DeleteFromProviderAsync(
        MediaAsset asset,
        IReadOnlyDictionary<Guid, FileStorageRef> storageRefs,
        CancellationToken cancellationToken)
    {
        if (asset.Kind == AssetKind.FILE)
        {
            // #646: delete any responsive variant objects first so they aren't orphaned.
            // MinIO / S3 return success for missing keys, so each delete is idempotent.
            foreach (ImageVariant variant in asset.ImageVariants)
            {
                UnitResult<Error> variantDelete =
                    await _objectStorageProvider.DeleteAsync(variant.StorageKey, cancellationToken);
                if (variantDelete.IsFailure)
                {
                    // Surface failure so the asset stays DELETING and the sweep retries —
                    // matches the original-object delete-retry semantics.
                    return variantDelete;
                }
            }

            // MinIO / S3 return success for missing keys, so this is idempotent.
            return storageRefs.TryGetValue(asset.Id, out FileStorageRef? storageRef)
                ? await _objectStorageProvider.DeleteAsync(storageRef.StorageKey.Value, cancellationToken)
                : UnitResult.Success<Error>();
        }

        // VIDEO: НЕ удаляем из Kinescope. Видео — платный ресурс, авто-сноc
        // (auto-replace previous, detach при unbind, FileMaintenance sweep) приводил
        // к потере оригинала после непреднамеренных flow'ов (refresh между upload и
        // save, например). Оставляем файл в Kinescope и `video_provider_refs` —
        // asset переходит в DELETED, но физический файл и ссылка живут.
        // Cleanup Kinescope storage — отдельная owner-action операция (не реализовано).
        return UnitResult.Success<Error>();
    }
}
