using System.Linq.Expressions;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Database;

public interface IIssueReviewSpecsRepository
{
    Task AddAsync(IssueReviewSpec spec, CancellationToken ct = default);

    Task<IssueReviewSpec?> GetByAsync(
        Expression<Func<IssueReviewSpec, bool>> predicate, CancellationToken ct = default);

    Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken ct = default);
}
