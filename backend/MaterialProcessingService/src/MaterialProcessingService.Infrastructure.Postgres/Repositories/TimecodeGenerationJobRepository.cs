using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.Timecodes;

namespace MaterialProcessingService.Infrastructure.Postgres.Repositories;

public sealed class TimecodeGenerationJobRepository : ITimecodeGenerationJobRepository
{
    private readonly MaterialProcessingServiceDbContext _context;

    public TimecodeGenerationJobRepository(MaterialProcessingServiceDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TimecodeGenerationJob job, CancellationToken cancellationToken = default)
    {
        await _context.TimecodeGenerationJobs.AddAsync(job, cancellationToken);
    }

    public Task<TimecodeGenerationJob?> GetActiveByVideoAsync(
        Guid videoId,
        CancellationToken cancellationToken = default) =>
        _context.TimecodeGenerationJobs
            .Where(x => x.VideoAssetId == videoId &&
                (x.Status == TimecodeGenerationStatus.Queued ||
                 x.Status == TimecodeGenerationStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<TimecodeGenerationJob?> GetByAsync(
        Expression<Func<TimecodeGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _context.TimecodeGenerationJobs
            .Where(predicate)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Expression<Func<TimecodeGenerationJob, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _context.TimecodeGenerationJobs
            .AsNoTracking()
            .AnyAsync(predicate, cancellationToken);

    public async Task<IReadOnlyList<TimecodeGenerationJob>> GetStuckJobsAsync(
        DateTime updatedBefore,
        int batchSize,
        CancellationToken cancellationToken = default) =>
        await _context.TimecodeGenerationJobs
            .Where(x => x.Status == TimecodeGenerationStatus.Processing
                        && x.UpdatedAt < updatedBefore)
            .OrderBy(x => x.UpdatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimecodeGenerationJob>> GetActiveByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _context.TimecodeGenerationJobs
            .AsNoTracking()
            .Where(x => x.RequestedByUserId == userId
                        && (x.Status == TimecodeGenerationStatus.Queued
                            || x.Status == TimecodeGenerationStatus.Processing))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetVideoIdsWithCompletedTimecodesAsync(
        IReadOnlyCollection<Guid> videoIds,
        Guid? requestedByUserId,
        CancellationToken cancellationToken = default)
    {
        if (videoIds.Count == 0)
            return new HashSet<Guid>();

        List<Guid> rows = await _context.TimecodeGenerationJobs
            .AsNoTracking()
            .Where(x => videoIds.Contains(x.VideoAssetId)
                        && x.Status == TimecodeGenerationStatus.Completed
                        && (!requestedByUserId.HasValue || x.RequestedByUserId == requestedByUserId.Value))
            .Select(x => x.VideoAssetId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows.ToHashSet();
    }
}
