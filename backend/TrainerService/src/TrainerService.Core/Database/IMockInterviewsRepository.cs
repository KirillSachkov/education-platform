using System.Linq.Expressions;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Core.Database;

public interface IMockInterviewsRepository
{
    Task AddAsync(MockInterview mockInterview, CancellationToken ct = default);

    Task<Result<MockInterview, Error>> GetByAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<MockInterview>> GetManyByAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default);

    Task<int?> GetMaxSortIndexAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default);
}
