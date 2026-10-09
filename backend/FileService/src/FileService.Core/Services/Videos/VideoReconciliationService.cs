using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Services.Videos;

public sealed class VideoReconciliationService
{
    private static bool AreMetadataEqual(VideoProviderMetadata? current, VideoProviderMetadata next) =>
        string.Equals(current?.ThumbnailUrl, next.ThumbnailUrl, StringComparison.Ordinal) &&
        current?.DurationSeconds == next.DurationSeconds &&
        current?.Width == next.Width &&
        current?.Height == next.Height;

    private readonly ILogger<VideoReconciliationService> _logger;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IVideoProviderRefRepository _videoProviderRefRepository;
    private readonly IVideoProvider _videoProvider;
    private readonly ITransactionManager _transactionManager;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly VideoMaintenanceOptions _options;
    private readonly FileServiceMetrics _metrics;

    public VideoReconciliationService(
        ILogger<VideoReconciliationService> logger,
        IMediaAssetRepository assetRepository,
        IVideoProviderRefRepository videoProviderRefRepository,
        IVideoProvider videoProvider,
        ITransactionManager transactionManager,
        AssetBindEventPublisher bindEventPublisher,
        IOptions<VideoMaintenanceOptions> options,
        FileServiceMetrics metrics)
    {
        _logger = logger;
        _assetRepository = assetRepository;
        _videoProviderRefRepository = videoProviderRefRepository;
        _videoProvider = videoProvider;
        _transactionManager = transactionManager;
        _bindEventPublisher = bindEventPublisher;
        _options = options.Value;
        _metrics = metrics;
    }

    public async Task<int> ReconcileVideosAsync(CancellationToken cancellationToken)
    {
        DateTime utcNow = DateTime.UtcNow;
        List<MediaAsset> assets = await _assetRepository.GetVideoReconciliationBatchAsync(
            _options.ReconciliationBatchSize,
            utcNow,
            cancellationToken);

        if (assets.Count == 0)
        {
            return 0;
        }

        IReadOnlyDictionary<Guid, VideoProviderRef> providerRefs =
            await _videoProviderRefRepository.GetByAssetIdsAsync(assets.Select(x => x.Id).ToArray(), cancellationToken);

        List<MediaAsset> assetsWithProviderRef = assets
            .Where(asset => providerRefs.ContainsKey(asset.Id))
            .ToList();

        if (assetsWithProviderRef.Count == 0)
        {
            return 0;
        }

        Result<IReadOnlyDictionary<string, VideoProviderAssetInfo>, Error> statusesResult = await _videoProvider.GetStatusesBatchAsync(
            assetsWithProviderRef
                .Select(asset => providerRefs[asset.Id].ExternalAssetId)
                .Distinct()
                .ToList(),
            cancellationToken);

        if (statusesResult.IsFailure)
        {
            _logger.LogWarning("Video reconciliation batch request failed: {ErrorType}", statusesResult.Error.Type);
            return 0;
        }

        IReadOnlyDictionary<string, VideoProviderAssetInfo> providerStatuses = statusesResult.Value;
        int mutatedCount = 0;

        foreach (MediaAsset asset in assetsWithProviderRef)
        {
            VideoProviderRef providerRef = providerRefs[asset.Id];
            bool mutated = false;

            if (!providerStatuses.TryGetValue(providerRef.ExternalAssetId, out VideoProviderAssetInfo? info))
            {
                if (asset.Status == AssetStatus.DELETING)
                {
                    _metrics.RecordDeletingStuck(1);
                }
                else if (ShouldMarkVideoFailed(asset, utcNow))
                {
                    UnitResult<Error> markFailedResult = asset.MarkFailed("Video reconciliation timeout");
                    if (markFailedResult.IsSuccess)
                    {
                        mutated = true;
                    }
                }

                if (mutated)
                {
                    mutatedCount++;
                }

                continue;
            }

            // Terminal assets may transfer their provider ref to a newly attached asset.
            // Do not mutate the old ref while reattach is deleting/replacing it (#886).
            if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
            {
                continue;
            }

            VideoProviderMetadata newMetadata = new()
            {
                ThumbnailUrl = info.ThumbnailUrl,
                DurationSeconds = info.Duration,
                Width = info.Width,
                Height = info.Height,
            };

            bool metadataChanged = !AreMetadataEqual(providerRef.Metadata, newMetadata);
            if (metadataChanged)
            {
                providerRef.UpdateMetadata(newMetadata);
                mutated = true;
            }

            AssetStatus newStatus = KinescopeStatusMapper.MapToAssetStatus(info.Status);
            AssetStatus previousStatus = asset.Status;

            UnitResult<Error> transitionResult = newStatus switch
            {
                AssetStatus.PROCESSING when asset.Status != AssetStatus.PROCESSING => asset.MarkProcessing(),
                AssetStatus.READY when asset.Status != AssetStatus.READY => asset.MarkReady(),
                AssetStatus.FAILED when asset.Status != AssetStatus.FAILED => asset.MarkFailed("Video provider reported failure"),
                _ => UnitResult.Success<Error>(),
            };

            bool transitioned = transitionResult.IsSuccess && asset.Status != previousStatus;
            if (transitioned)
            {
                mutated = true;
                _metrics.RecordReconciliationRepaired(1);

                if (newStatus == AssetStatus.READY && asset.TargetEntity is not null)
                {
                    Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
                        asset,
                        asset.TargetEntity,
                        advanceBindingRevision: false,
                        cancellationToken: cancellationToken);
                    if (binding.IsFailure)
                    {
                        _logger.LogWarning(
                            "Could not publish binding revision for reconciled asset {AssetId}: {ErrorType}",
                            asset.Id,
                            binding.Error.Type);
                        continue;
                    }
                }
            }

            if (mutated)
            {
                mutatedCount++;
            }
        }

        if (mutatedCount == 0)
        {
            return 0;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError("Failed to persist video reconciliation changes: {ErrorType}", saveResult.Error.Type);
            return 0;
        }

        return mutatedCount;
    }

    private bool ShouldMarkVideoFailed(MediaAsset asset, DateTime utcNow) =>
        asset.Status switch
        {
            AssetStatus.PENDING_UPLOAD =>
                asset.CreatedAt <= utcNow.AddHours(-_options.VideoUploadTimeoutHours) &&
                asset.IsStale(utcNow, AssetStaleWorkflowType.VideoUpload),
            AssetStatus.PROCESSING =>
                asset.ProcessingStartedAt != null &&
                asset.ProcessingStartedAt <= utcNow.AddHours(-_options.VideoProcessingTimeoutHours) &&
                asset.IsStale(utcNow, AssetStaleWorkflowType.VideoProcessing),
            _ => false,
        };
}