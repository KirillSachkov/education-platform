using System.Linq.Expressions;
using FileService.Core.Repositories;
using FileService.Domain;

namespace FileService.Infrastructure.Postgres.Repositories;

public sealed class MediaAssetRepository : IMediaAssetRepository
{
    private readonly FileServiceDbContext _context;

    public MediaAssetRepository(FileServiceDbContext context) => _context = context;

    public async Task AddAsync(MediaAsset asset, CancellationToken ct = default)
    {
        await _context.MediaAssets.AddAsync(asset, ct);
    }

    public async Task<long> GetNextBindingRevisionAsync(CancellationToken ct = default) =>
        await _context.Database
            .SqlQueryRaw<long>("SELECT nextval('files.asset_binding_revision_seq') AS \"Value\"")
            .SingleAsync(ct);

    public async Task<Result<MediaAsset, Error>> GetByAsync(
        Expression<Func<MediaAsset, bool>> predicate,
        CancellationToken ct = default)
    {
        MediaAsset? asset = await _context.MediaAssets.FirstOrDefaultAsync(predicate, ct);
        if (asset is null)
        {
            return GeneralErrors.NotFound();
        }

        return asset;
    }

    public async Task<List<MediaAsset>> GetManyByAsync(
        Expression<Func<MediaAsset, bool>> predicate,
        CancellationToken ct = default) => await _context.MediaAssets.Where(predicate).ToListAsync(ct);

    public async Task<List<MediaAsset>> GetReadyAssetsByEntityAndUsageAsync(
        string targetEntityType,
        Guid targetEntityId,
        AssetUsageType usageType,
        Guid excludeAssetId,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(a => a.Id != excludeAssetId
                        && a.TargetEntity != null
                        && a.TargetEntity.Type == targetEntityType
                        && a.TargetEntity.Id == targetEntityId
                        && a.UsageType == usageType
                        && a.Status == AssetStatus.READY)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetReadyAssetsByEntityAndUsageTypesAsync(
        string targetEntityType,
        Guid targetEntityId,
        IReadOnlyCollection<AssetUsageType> usageTypes,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(a => a.TargetEntity != null
                        && a.TargetEntity.Type == targetEntityType
                        && a.TargetEntity.Id == targetEntityId
                        && usageTypes.Contains(a.UsageType)
                        && a.Status == AssetStatus.READY)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetByTargetEntitiesAsync(
        IReadOnlyCollection<TargetEntity> targetEntities,
        CancellationToken ct = default)
    {
        if (targetEntities.Count == 0)
        {
            return [];
        }

        Guid[] ids = targetEntities.Select(e => e.Id).Distinct().ToArray();
        string[] types = targetEntities.Select(e => e.Type).Distinct().ToArray();
        HashSet<(string Type, Guid Id)> exactTargets = targetEntities
            .Select(e => (e.Type, e.Id))
            .ToHashSet();

        List<MediaAsset> assets = await _context.MediaAssets
            .Where(a => a.TargetEntity != null
                        && ids.Contains(a.TargetEntity.Id)
                        && types.Contains(a.TargetEntity.Type)
                        && a.Status != AssetStatus.DELETED
                        && a.Status != AssetStatus.DELETING)
            .ToListAsync(ct);

        return assets
            .Where(a => a.TargetEntity is not null
                        && exactTargets.Contains((a.TargetEntity.Type, a.TargetEntity.Id)))
            .ToList();
    }

    public async Task<List<MediaAsset>> GetManyByOrderedAsync<TKey>(
        Expression<Func<MediaAsset, bool>> predicate,
        Expression<Func<MediaAsset, TKey>> orderByDescending,
        int take,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(predicate)
            .OrderByDescending(orderByDescending)
            .Take(take)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetByDraftIdAsync(Guid draftId, CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.DraftId == draftId)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetActiveDraftAssetsAsync(Guid draftId, int take = 100, CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.DraftId == draftId
                        && x.Status != AssetStatus.DELETED
                        && x.Status != AssetStatus.DELETING)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<MediaAsset?> GetActiveSlotAssetAsync(
        string entityType,
        Guid entityId,
        AssetUsageType usageType,
        CancellationToken ct = default) =>
        _context.MediaAssets
            .Where(asset =>
                asset.TargetEntity != null &&
                asset.TargetEntity.Type == entityType &&
                asset.TargetEntity.Id == entityId &&
                asset.UsageType == usageType &&
                !asset.IsTemporary &&
                asset.ConfirmedBindingRevision > asset.DetachedThroughBindingRevision &&
                asset.Status != AssetStatus.DELETING &&
                asset.Status != AssetStatus.DELETED)
            .OrderByDescending(asset => asset.ConfirmedBindingRevision)
            .ThenByDescending(asset => asset.BindingRevision)
            .ThenByDescending(asset => asset.BoundAt ?? asset.CreatedAt)
            .ThenByDescending(asset => asset.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<List<MediaAsset>> GetPendingFileCleanupBatchAsync(
        int batchSize,
        DateTime cutoff,
        CancellationToken ct = default) =>
        await _context.MediaAssets.Where(x => x.Kind == AssetKind.FILE &&
                                              x.Status == AssetStatus.PENDING_UPLOAD &&
                                              x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetDraftFileCleanupBatchAsync(
        int batchSize,
        DateTime cutoff,
        CancellationToken ct = default) =>
        await _context.MediaAssets.Where(x => x.Kind == AssetKind.FILE &&
                                              (x.UsageType == AssetUsageType.MARKDOWN_IMAGE ||
                                               x.UsageType == AssetUsageType.MARKDOWN_FILE ||
                                               x.UsageType == AssetUsageType.MATERIAL_PREVIEW) &&
                                              x.IsTemporary &&
                                              x.DraftId != null &&
                                              x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetOrphanDraftCleanupBatchAsync(
        int batchSize,
        DateTime cutoff,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.DraftId != null &&
                        x.TargetEntity == null &&
                        x.Status != AssetStatus.DELETED &&
                        x.Status != AssetStatus.DELETING &&
                        x.CreatedAt <= cutoff)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetVideoReconciliationBatchAsync(
        int batchSize,
        DateTime utcNow,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.Kind == AssetKind.VIDEO &&
                        (x.Status == AssetStatus.PENDING_UPLOAD ||
                         x.Status == AssetStatus.PROCESSING ||
                         x.Status == AssetStatus.DELETING))
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetDeletingRetentionBatchAsync(
        int batchSize,
        DateTime cutoff,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.Status == AssetStatus.DELETING && x.DeleteRequestedAt != null &&
                        x.DeleteRequestedAt <= cutoff)
            .OrderBy(x => x.DeleteRequestedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetDeletedPurgeBatchAsync(
        int batchSize,
        DateTime cutoff,
        CancellationToken ct = default) =>
        await _context.MediaAssets
            .Where(x => x.Kind == AssetKind.FILE &&
                        x.Status == AssetStatus.DELETED &&
                        (x.UsageType == AssetUsageType.MARKDOWN_IMAGE ||
                         x.UsageType == AssetUsageType.MARKDOWN_FILE ||
                         x.BindingRevision <= x.ConfirmedBindingRevision) &&
                        x.DeletedAt != null &&
                        x.DeletedAt <= cutoff)
            .OrderBy(x => x.DeletedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<List<MediaAsset>> GetReadyFileAssetsBatchAsync(
        int batchSize,
        DateTime afterCreatedAt,
        CancellationToken ct = default) =>
        // Translatable predicate only — ContentType is a value-object behind a converter,
        // so the image-content-type narrowing happens in-app at the call site (#646). The
        // CreatedAt cursor advances by the true last row of THIS SQL page (not the filtered
        // subset), so the backfill loop can't stall on a page of non-images.
        await _context.MediaAssets
            .Where(x => x.Kind == AssetKind.FILE
                        && x.Status == AssetStatus.READY
                        && x.CreatedAt > afterCreatedAt)
            .OrderBy(x => x.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public Task DeleteAsync(MediaAsset asset, CancellationToken ct = default)
    {
        _context.MediaAssets.Remove(asset);
        return Task.CompletedTask;
    }
}
