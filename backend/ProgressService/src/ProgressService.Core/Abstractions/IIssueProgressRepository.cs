using System.Linq.Expressions;
using ProgressService.Domain.Issues;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий прогресса по задачам.
/// </summary>
public interface IIssueProgressRepository
{
    Task AddAsync(IssueProgress issueProgress, CancellationToken cancellationToken = default);

    Task<Result<IssueProgress, Error>> GetByAsync(
        Expression<Func<IssueProgress, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет весь прогресс по задаче (issue_submissions каскадно удалятся по FK).
    ///     Используется обработчиком <c>IssueHardDeleted</c>.
    /// </summary>
    Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken cancellationToken = default);
}
