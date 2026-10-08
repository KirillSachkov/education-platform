using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.ContentDrafts;

namespace MaterialProcessingService.Infrastructure.Postgres.Repositories;

public sealed class ContentGenerationJobRepository : IContentGenerationJobRepository
{
    private readonly MaterialProcessingServiceDbContext _context;

    public ContentGenerationJobRepository(MaterialProcessingServiceDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ContentGenerationJob job, CancellationToken cancellationToken = default)
    {
        await _context.ContentGenerationJobs.AddAsync(job, cancellationToken);
    }

    public Task<ContentGenerationJob?> GetActiveByVideoAndMaterialAsync(
        Guid videoId,
        Guid materialId,
        CancellationToken cancellationToken = default) =>
        _context.ContentGenerationJobs
            .Where(x => x.VideoAssetId == videoId &&
                x.MaterialId == materialId &&
                (x.Status == ContentGenerationStatus.Queued ||
                 x.Status == ContentGenerationStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ContentGenerationJob?> GetByAsync(
        Expression<Func<ContentGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _context.ContentGenerationJobs
            .Where(predicate)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ContentGenerationJob>> GetStuckJobsAsync(
        DateTime updatedBefore,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await _context.ContentGenerationJobs
            .Where(x => x.Status == ContentGenerationStatus.Processing
                        && x.UpdatedAt < updatedBefore)
            .OrderBy(x => x.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ContentGenerationJob>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _context.ContentGenerationJobs
            .AsNoTracking()
            .Where(x => x.RequestedByUserId == userId
                        && (x.Status == ContentGenerationStatus.Queued
                            || x.Status == ContentGenerationStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetMaterialIdsWithCompletedSummaryAsync(
        IReadOnlyCollection<Guid> materialIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (materialIds.Count == 0)
            return new HashSet<Guid>();

        List<Guid> rows = await _context.ContentGenerationJobs
            .AsNoTracking()
            .Where(x => materialIds.Contains(x.MaterialId)
                        && x.Status == ContentGenerationStatus.Completed
                        && (!requestedByUserId.HasValue || x.RequestedByUserId == requestedByUserId.Value))
            .Select(x => x.MaterialId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.ToHashSet();
    }
}
