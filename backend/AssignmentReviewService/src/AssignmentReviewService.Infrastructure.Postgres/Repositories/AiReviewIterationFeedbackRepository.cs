using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class AiReviewIterationFeedbackRepository : IAiReviewIterationFeedbackRepository
{
    private readonly AssignmentReviewServiceDbContext _db;

    public AiReviewIterationFeedbackRepository(AssignmentReviewServiceDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(AiReviewIterationFeedback feedback, CancellationToken ct = default) =>
        await _db.AiReviewIterationFeedbacks.AddAsync(feedback, ct);

    public Task<AiReviewIterationFeedback?> GetByAsync(
        Expression<Func<AiReviewIterationFeedback, bool>> predicate,
        CancellationToken ct = default) =>
        _db.AiReviewIterationFeedbacks.FirstOrDefaultAsync(predicate, ct);
}
