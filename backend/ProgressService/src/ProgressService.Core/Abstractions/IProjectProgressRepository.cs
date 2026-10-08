using System.Linq.Expressions;
using ProgressService.Domain.Projects;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий прогресса по проектам.
/// </summary>
public interface IProjectProgressRepository
{
    Task AddAsync(ProjectProgress projectProgress, CancellationToken cancellationToken = default);

    Task<Result<ProjectProgress, Error>> GetByAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectProgress>> GetManyByAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default);
}
