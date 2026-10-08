using System.Linq.Expressions;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Database;

/// <summary>
///     Repository для <see cref="AiReviewIterationFeedback"/>. Issue #327.
/// </summary>
public interface IAiReviewIterationFeedbackRepository
{
    Task AddAsync(AiReviewIterationFeedback feedback, CancellationToken ct = default);

    Task<AiReviewIterationFeedback?> GetByAsync(
        Expression<Func<AiReviewIterationFeedback, bool>> predicate,
        CancellationToken ct = default);
}
