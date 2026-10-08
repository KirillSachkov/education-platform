using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class IssueReviewSpecsRepository : IIssueReviewSpecsRepository
{
    private readonly AssignmentReviewServiceDbContext _db;

    public IssueReviewSpecsRepository(AssignmentReviewServiceDbContext db) => _db = db;

    public async Task AddAsync(IssueReviewSpec spec, CancellationToken ct = default) =>
        await _db.IssueReviewSpecs.AddAsync(spec, ct);

    public Task<IssueReviewSpec?> GetByAsync(
        Expression<Func<IssueReviewSpec, bool>> predicate, CancellationToken ct = default) =>
        _db.IssueReviewSpecs.FirstOrDefaultAsync(predicate, ct);

    public Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken ct = default) =>
        _db.IssueReviewSpecs.Where(s => s.IssueId == issueId).ExecuteDeleteAsync(ct);
}
