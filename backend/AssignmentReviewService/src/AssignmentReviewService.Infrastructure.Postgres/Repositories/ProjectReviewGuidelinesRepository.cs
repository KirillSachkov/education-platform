using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class ProjectReviewGuidelinesRepository : IProjectReviewGuidelinesRepository
{
    private readonly AssignmentReviewServiceDbContext _db;

    public ProjectReviewGuidelinesRepository(AssignmentReviewServiceDbContext db) => _db = db;

    public async Task AddAsync(ProjectReviewGuidelines guidelines, CancellationToken ct = default) =>
        await _db.ProjectReviewGuidelines.AddAsync(guidelines, ct);

    public Task<ProjectReviewGuidelines?> GetByAsync(
        Expression<Func<ProjectReviewGuidelines, bool>> predicate, CancellationToken ct = default) =>
        _db.ProjectReviewGuidelines.FirstOrDefaultAsync(predicate, ct);
}
