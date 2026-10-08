using FileService.Core.Repositories;
using FileService.Domain;

namespace FileService.Infrastructure.Postgres.Repositories;

public sealed class AssetOwnershipCheckpointRepository : IAssetOwnershipCheckpointRepository
{
    private readonly FileServiceDbContext _context;

    public AssetOwnershipCheckpointRepository(FileServiceDbContext context) => _context = context;

    public async Task AcquireCourseLockAsync(Guid courseId, CancellationToken ct = default)
    {
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({courseId.ToString()}, 0))",
            ct);
    }

    public async Task AcquireTargetLocksAsync(
        IReadOnlyCollection<TargetEntity> targets,
        CancellationToken ct = default)
    {
        foreach (TargetEntity target in targets
                     .Distinct()
                     .OrderBy(target => target.Type, StringComparer.Ordinal)
                     .ThenBy(target => target.Id))
        {
            string lockKey = $"{target.Type.ToLowerInvariant()}:{target.Id:D}";
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                ct);
        }
    }

    public async Task<IReadOnlyList<AssetOwnershipCheckpoint>> GetManyAsync(
        IReadOnlyCollection<TargetEntity> targets,
        CancellationToken ct = default)
    {
        if (targets.Count == 0)
            return [];

        Guid[] targetIds = targets.Select(target => target.Id).Distinct().ToArray();
        string[] targetTypes = targets.Select(target => target.Type).Distinct().ToArray();
        HashSet<(string Type, Guid Id)> exactTargets = targets
            .Select(target => (target.Type, target.Id))
            .ToHashSet();

        List<AssetOwnershipCheckpoint> candidates = await _context.AssetOwnershipCheckpoints
            .Where(checkpoint => targetIds.Contains(checkpoint.TargetId)
                                 && targetTypes.Contains(checkpoint.TargetType))
            .ToListAsync(ct);

        return candidates
            .Where(checkpoint => exactTargets.Contains((checkpoint.TargetType, checkpoint.TargetId)))
            .ToArray();
    }

    public async Task AddAsync(AssetOwnershipCheckpoint checkpoint, CancellationToken ct = default) =>
        await _context.AssetOwnershipCheckpoints.AddAsync(checkpoint, ct);
}
