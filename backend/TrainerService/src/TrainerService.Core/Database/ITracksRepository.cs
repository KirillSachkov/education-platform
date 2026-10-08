using System.Linq.Expressions;
using TrainerService.Domain.Tracks;

namespace TrainerService.Core.Database;

public interface ITracksRepository
{
    Task AddAsync(Track track, CancellationToken ct = default);

    Task<Result<Track, Error>> GetByAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Track>> GetManyByAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default);

    Task<string?> GetMaxSortKeyAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<Track, bool>> predicate,
        CancellationToken ct = default);
}
