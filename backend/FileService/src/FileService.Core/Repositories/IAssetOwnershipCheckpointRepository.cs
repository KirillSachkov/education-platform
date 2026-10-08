using FileService.Domain;

namespace FileService.Core.Repositories;

public interface IAssetOwnershipCheckpointRepository
{
    Task AcquireCourseLockAsync(Guid courseId, CancellationToken ct = default);

    Task AcquireTargetLocksAsync(
        IReadOnlyCollection<TargetEntity> targets,
        CancellationToken ct = default);

    Task<IReadOnlyList<AssetOwnershipCheckpoint>> GetManyAsync(
        IReadOnlyCollection<TargetEntity> targets,
        CancellationToken ct = default);

    Task AddAsync(AssetOwnershipCheckpoint checkpoint, CancellationToken ct = default);
}
