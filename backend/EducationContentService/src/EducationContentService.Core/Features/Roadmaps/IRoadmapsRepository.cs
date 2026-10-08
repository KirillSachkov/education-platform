using System.Linq.Expressions;
using EducationContentService.Domain.Roadmaps;

namespace EducationContentService.Core.Features.Roadmaps;

public interface IRoadmapsRepository
{
    Task AddAsync(Roadmap roadmap, CancellationToken cancellationToken = default);

    Task<Result<Roadmap, Error>> GetByAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<Result<Roadmap, Error>> GetWithChildrenAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Roadmap roadmap, CancellationToken ct = default);
}
