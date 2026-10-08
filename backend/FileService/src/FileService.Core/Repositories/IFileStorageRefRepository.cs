using System.Linq.Expressions;
using FileService.Domain;

namespace FileService.Core.Repositories;

public interface IFileStorageRefRepository
{
    Task AddAsync(FileStorageRef storageRef, CancellationToken ct = default);

    Task<Result<FileStorageRef, Error>> GetByAsync(
        Expression<Func<FileStorageRef, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, FileStorageRef>> GetByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken ct = default);

    Task DeleteAsync(FileStorageRef storageRef, CancellationToken ct = default);
}
