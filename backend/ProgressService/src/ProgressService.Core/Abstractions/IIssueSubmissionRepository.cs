using System.Linq.Expressions;
using ProgressService.Domain.IssueSubmissions;

namespace ProgressService.Core.Abstractions;

/// <summary>
///     Репозиторий попыток сдачи задач.
/// </summary>
public interface IIssueSubmissionRepository
{
    Task AddAsync(IssueSubmission submission, CancellationToken cancellationToken = default);

    Task<Result<IssueSubmission, Error>> GetByAsync(
        Expression<Func<IssueSubmission, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<Result<int, Error>> GetMaxAttemptNumberAsync(Guid issueProgressId, CancellationToken cancellationToken = default);
}
