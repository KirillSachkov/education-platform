using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Tracks;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TracksRepository : ITracksRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TracksRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Track track, CancellationToken ct = default) =>
        await _dbContext.Tracks.AddAsync(track, ct);

    public async Task<Result<Track, Error>> GetByAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default)
    {
        Track? track = await _dbContext.Tracks.FirstOrDefaultAsync(predicate, ct);
        return track is null
            ? TrainerServiceErrors.Track.NotFound(Guid.Empty)
            : track;
    }

    public async Task<IReadOnlyList<Track>> GetManyByAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.Tracks.Where(predicate).ToListAsync(ct);

    public Task<string?> GetMaxSortKeyAsync(CancellationToken ct = default) =>
        _dbContext.Tracks
            .OrderByDescending(t => t.SortKey)
            .Select(t => t.SortKey)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.Tracks.AnyAsync(predicate, ct);
}
