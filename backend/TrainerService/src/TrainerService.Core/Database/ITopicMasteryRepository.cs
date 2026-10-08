using System.Linq.Expressions;
using TrainerService.Domain.TopicMasteries;

namespace TrainerService.Core.Database;

public interface ITopicMasteryRepository
{
    Task AddAsync(TopicMastery mastery, CancellationToken ct = default);

    Task<Result<TopicMastery, Error>> GetByAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<TopicMastery>> GetManyByAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default);
}
