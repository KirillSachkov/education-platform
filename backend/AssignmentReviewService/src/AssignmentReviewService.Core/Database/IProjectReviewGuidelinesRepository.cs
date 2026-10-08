using System.Linq.Expressions;
using AssignmentReviewService.Domain.Reviews;

namespace AssignmentReviewService.Core.Database;

public interface IProjectReviewGuidelinesRepository
{
    Task AddAsync(ProjectReviewGuidelines guidelines, CancellationToken ct = default);

    Task<ProjectReviewGuidelines?> GetByAsync(
        Expression<Func<ProjectReviewGuidelines, bool>> predicate, CancellationToken ct = default);
}
