using System.Linq.Expressions;
using FileService.Core.Repositories;
using FileService.Domain;

namespace FileService.Infrastructure.Postgres.Repositories;

public sealed class FileStorageRefRepository : IFileStorageRefRepository
{
    private readonly FileServiceDbContext _context;

    public FileStorageRefRepository(FileServiceDbContext context) => _context = context;

    public async Task AddAsync(FileStorageRef storageRef, CancellationToken ct = default)
    {
        await _context.FileStorageRefs.AddAsync(storageRef, ct);
    }

    public async Task<Result<FileStorageRef, Error>> GetByAsync(
        Expression<Func<FileStorageRef, bool>> predicate,
        CancellationToken ct = default)
    {
        FileStorageRef? storageRef = await _context.FileStorageRefs.FirstOrDefaultAsync(predicate, ct);
        if (storageRef is null)
        {
            return GeneralErrors.NotFound();
        }

        return storageRef;
    }

    public async Task<IReadOnlyDictionary<Guid, FileStorageRef>> GetByAssetIdsAsync(
        IReadOnlyCollection<Guid> assetIds,
        CancellationToken ct = default)
    {
        if (assetIds.Count == 0)
        {
            return new Dictionary<Guid, FileStorageRef>();
        }

        return await _context.FileStorageRefs
            .Where(x => assetIds.Contains(x.AssetId))
            .ToDictionaryAsync(x => x.AssetId, ct);
    }

    public Task DeleteAsync(FileStorageRef storageRef, CancellationToken ct = default)
    {
        _context.FileStorageRefs.Remove(storageRef);
        return Task.CompletedTask;
    }
}
