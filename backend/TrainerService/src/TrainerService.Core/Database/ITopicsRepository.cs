using System.Linq.Expressions;
using TrainerService.Domain.Topics;

namespace TrainerService.Core.Database;

public interface ITopicsRepository
{
    Task AddAsync(Topic topic, CancellationToken ct = default);

    Task RemoveAsync(Topic topic, CancellationToken ct = default);

    Task<Result<Topic, Error>> GetByAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Topic>> GetManyByAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default);

    Task<string?> GetMaxSortKeyAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default);
}
