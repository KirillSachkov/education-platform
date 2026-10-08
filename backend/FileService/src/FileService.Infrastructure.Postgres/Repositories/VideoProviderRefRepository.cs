using System.Linq.Expressions;
using FileService.Core.Repositories;
using FileService.Domain;

namespace FileService.Infrastructure.Postgres.Repositories;

public sealed class VideoProviderRefRepository : IVideoProviderRefRepository
{
    private readonly FileServiceDbContext _context;

    public VideoProviderRefRepository(FileServiceDbContext context) => _context = context;

    public async Task AddAsync(VideoProviderRef providerRef, CancellationToken ct = default)
    {
        await _context.VideoProviderRefs.AddAsync(providerRef, ct);
    }

    public async Task<Result<VideoProviderRef, Error>> GetByAsync(
        Expression<Func<VideoProviderRef, bool>> predicate,
        CancellationToken ct = default)
    {
        VideoProviderRef? providerRef = await _context.VideoProviderRefs.FirstOrDefaultAsync(predicate, ct);
        if (providerRef is null)
        {
            return GeneralErrors.NotFound();
        }

        return providerRef;
    }

    public async Task<IReadOnlyDictionary<Guid, VideoProviderRef>> GetByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken ct = default)
    {
        if (assetIds.Count == 0)
        {
            return new Dictionary<Guid, VideoProviderRef>();
        }

        return await _context.VideoProviderRefs
            .Where(x => assetIds.Contains(x.AssetId))
            .ToDictionaryAsync(x => x.AssetId, ct);
    }

    public async Task<bool> HasExternalAssetOwnedByAnotherUserAsync(
        string externalAssetId,
        Guid userId,
        CancellationToken ct = default) =>
        await _context.VideoProviderRefs
            .Where(providerRef => providerRef.ExternalAssetId == externalAssetId)
            .Join(
                _context.MediaAssets,
                providerRef => providerRef.AssetId,
                asset => asset.Id,
                (_, asset) => asset)
            .AnyAsync(
                asset => asset.UploadedByUserId == null || asset.UploadedByUserId != userId,
                ct);

    public async Task AcquireExternalAssetLockAsync(
        string externalAssetId,
        CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({externalAssetId}, 0))",
            ct);
    }

    public Task DeleteAsync(VideoProviderRef providerRef, CancellationToken ct = default)
    {
        _context.VideoProviderRefs.Remove(providerRef);
        return Task.CompletedTask;
    }
}
