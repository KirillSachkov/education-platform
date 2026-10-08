using System.Linq.Expressions;
using FileService.Domain;

namespace FileService.Core.Repositories;

public interface IVideoProviderRefRepository
{
    Task AddAsync(VideoProviderRef providerRef, CancellationToken ct = default);

    Task<Result<VideoProviderRef, Error>> GetByAsync(
        Expression<Func<VideoProviderRef, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, VideoProviderRef>> GetByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken ct = default);

    Task<bool> HasExternalAssetOwnedByAnotherUserAsync(
        string externalAssetId,
        Guid userId,
        CancellationToken ct = default);

    Task AcquireExternalAssetLockAsync(string externalAssetId, CancellationToken ct = default);

    Task DeleteAsync(VideoProviderRef providerRef, CancellationToken ct = default);
}
