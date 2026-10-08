using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Domain;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Services.AssetRegistry;

public sealed class AssetDeletionLifecycleService
{
    private readonly ILogger<AssetDeletionLifecycleService> _logger;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;

    public AssetDeletionLifecycleService(
        ILogger<AssetDeletionLifecycleService> logger,
        IMediaAssetRepository assetRepository,
        IOutboxService outbox,
        ITransactionManager transactionManager)
    {
        _logger = logger;
        _assetRepository = assetRepository;
        _outbox = outbox;
        _transactionManager = transactionManager;
    }

    public async Task<int> DeleteByTargetEntityAsync(
        string targetEntityType,
        Guid targetEntityId,
        CancellationToken cancellationToken)
    {
        return await DeleteByTargetEntitiesBatchAsync(
            [(targetEntityType, targetEntityId)],
            cancellationToken);
    }

    /// <summary>
    ///     Пакетное удаление ресурсов для нескольких сущностей за одну транзакцию.
    ///     Загружает все ресурсы одним запросом, а не N+1.
    /// </summary>
    public async Task<int> DeleteByTargetEntitiesBatchAsync(
        IReadOnlyList<(string EntityType, Guid EntityId)> entities,
        CancellationToken cancellationToken)
    {
        if (entities.Count == 0)
        {
            return 0;
        }

        // Collect all entity IDs grouped by type for a batch query
        HashSet<Guid> allEntityIds = entities.Select(e => e.EntityId).ToHashSet();
        HashSet<string> allEntityTypes = entities.Select(e => e.EntityType).ToHashSet(StringComparer.Ordinal);

        List<MediaAsset> assets = await _assetRepository.GetManyByAsync(
            asset => asset.TargetEntity != null &&
                     allEntityTypes.Contains(asset.TargetEntity.Type) &&
                     allEntityIds.Contains(asset.TargetEntity.Id),
            cancellationToken);

        if (assets.Count == 0)
        {
            return 0;
        }

        // Filter to only matching entity pairs (type+id)
        HashSet<(string, Guid)> entitySet = entities.ToHashSet();
        assets = assets
            .Where(a => a.TargetEntity is not null &&
                        entitySet.Contains((a.TargetEntity.Type, a.TargetEntity.Id)))
            .ToList();

        int deletedCount = 0;

        foreach (MediaAsset asset in assets)
        {
            UnitResult<Error> requestDeleteResult = asset.RequestDelete();
            if (requestDeleteResult.IsFailure)
            {
                _logger.LogWarning(
                    "Skipping delete request for asset {AssetId}: {ErrorType}",
                    asset.Id,
                    requestDeleteResult.Error.Type);
                continue;
            }

            deletedCount++;

            await _outbox.PublishAsync(new FileAssetDeleted(
                asset.Id,
                asset.Kind.ToString().ToLowerInvariant(),
                asset.UsageType.ToApiString(),
                asset.TargetEntity?.Id,
                asset.TargetEntity?.Type,
                asset.GetDeletionBindingRevision()));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save batch deletion changes for {EntityCount} entities: {ErrorType}",
                entities.Count,
                saveResult.Error.Type);
            throw new InvalidOperationException("Failed to persist asset deletion lifecycle");
        }

        return deletedCount;
    }
}
