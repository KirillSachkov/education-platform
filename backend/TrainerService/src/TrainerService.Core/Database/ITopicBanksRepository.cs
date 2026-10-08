using System.Linq.Expressions;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Core.Database;

public interface ITopicBanksRepository
{
    Task AddAsync(TopicBank bank, CancellationToken ct = default);

    Task RemoveAsync(TopicBank bank, CancellationToken ct = default);

    Task<Result<TopicBank, Error>> GetByAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<TopicBank>> GetManyByAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default);

    Task<string?> GetMaxSortKeyAsync(Guid topicId, CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default);
}
