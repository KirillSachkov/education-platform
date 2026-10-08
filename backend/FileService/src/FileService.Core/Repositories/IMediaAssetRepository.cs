using System.Linq.Expressions;
using FileService.Domain;

namespace FileService.Core.Repositories;

public interface IMediaAssetRepository
{
    Task<long> GetNextBindingRevisionAsync(CancellationToken ct = default);

    Task AddAsync(MediaAsset asset, CancellationToken ct = default);

    Task<Result<MediaAsset, Error>> GetByAsync(
        Expression<Func<MediaAsset, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     Generic ad-hoc query. Prefer a named method when a new common access pattern emerges
    ///     so that index planning stays explicit. Existing callers:
    ///     <see cref="AssetDeletionLifecycleService"/> (by target entity, no status filter) and
    ///     <see cref="SyncEntityAssetsHandler"/> (by target entity + usage types + ready).
    /// </summary>
    Task<List<MediaAsset>> GetManyByAsync(Expression<Func<MediaAsset, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    ///     Returns all Ready assets bound to the given target entity and usage type.
    ///     Backed by the composite index on (target_entity_type, target_entity_id, usage_type, status).
    ///     Used by <see cref="CompleteFileUploadHandler"/> to locate previous assets to supersede
    ///     when completing an upload for an <c>EntityRequired</c> usage type.
    /// </summary>
    Task<List<MediaAsset>> GetReadyAssetsByEntityAndUsageAsync(
        string targetEntityType,
        Guid targetEntityId,
        AssetUsageType usageType,
        Guid excludeAssetId,
        CancellationToken ct = default);

    /// <summary>
    ///     Batch-returns all Ready assets for the given target entity across multiple usage types.
    ///     Single DB round-trip instead of N per-usage-type queries.
    /// </summary>
    Task<List<MediaAsset>> GetReadyAssetsByEntityAndUsageTypesAsync(
        string targetEntityType,
        Guid targetEntityId,
        IReadOnlyCollection<AssetUsageType> usageTypes,
        CancellationToken ct = default);

    Task<List<MediaAsset>> GetByTargetEntitiesAsync(
        IReadOnlyCollection<TargetEntity> targetEntities,
        CancellationToken ct = default);

    Task<List<MediaAsset>> GetManyByOrderedAsync<TKey>(
        Expression<Func<MediaAsset, bool>> predicate,
        Expression<Func<MediaAsset, TKey>> orderByDescending,
        int take,
        CancellationToken ct = default);

    Task<List<MediaAsset>> GetByDraftIdAsync(Guid draftId, CancellationToken ct = default);

    Task<List<MediaAsset>> GetActiveDraftAssetsAsync(Guid draftId, int take = 100, CancellationToken ct = default);

    Task<MediaAsset?> GetActiveSlotAssetAsync(
        string entityType,
        Guid entityId,
        AssetUsageType usageType,
        CancellationToken ct = default);

    Task<List<MediaAsset>> GetPendingFileCleanupBatchAsync(int batchSize, DateTime cutoff, CancellationToken ct = default);

    Task<List<MediaAsset>> GetDraftFileCleanupBatchAsync(int batchSize, DateTime cutoff, CancellationToken ct = default);

    /// <summary>
    ///     Returns draft assets older than <paramref name="cutoff"/> that were never bound
    ///     to a target entity. Covers all asset kinds (files and videos) — a safety net
    ///     for drafts that the 24-hour <see cref="GetDraftFileCleanupBatchAsync"/> missed
    ///     (e.g., video drafts, or edge cases where the quick cleanup filter didn't match).
    /// </summary>
    Task<List<MediaAsset>> GetOrphanDraftCleanupBatchAsync(int batchSize, DateTime cutoff, CancellationToken ct = default);

    Task<List<MediaAsset>> GetVideoReconciliationBatchAsync(int batchSize, DateTime utcNow, CancellationToken ct = default);

    Task<List<MediaAsset>> GetDeletingRetentionBatchAsync(int batchSize, DateTime cutoff, CancellationToken ct = default);

    Task<List<MediaAsset>> GetDeletedPurgeBatchAsync(int batchSize, DateTime cutoff, CancellationToken ct = default);

    /// <summary>
    ///     Returns a forward page of READY FILE assets created after
    ///     <paramref name="afterCreatedAt"/>, ordered by CreatedAt. Used by the
    ///     <c>generate-image-variants</c> backfill CLI (#646). The image-content-type
    ///     narrowing and variant-presence check are applied in-app at the call site
    ///     (ContentType value-object + variants JSONB are not LINQ-translatable); the
    ///     CreatedAt cursor advances by the true last row of the SQL page so the loop
    ///     can't stall on a page of non-image files.
    /// </summary>
    Task<List<MediaAsset>> GetReadyFileAssetsBatchAsync(
        int batchSize,
        DateTime afterCreatedAt,
        CancellationToken ct = default);

    Task DeleteAsync(MediaAsset asset, CancellationToken ct = default);
}
