using System.Linq.Expressions;
using ProgressService.Domain.Users;

namespace ProgressService.Core.Abstractions;

public interface IProgressUserRepository
{
    Task AddAsync(ProgressUser user, CancellationToken cancellationToken = default);

    Task<Result<ProgressUser, Error>> GetByAsync(
        Expression<Func<ProgressUser, bool>> predicate,
        CancellationToken cancellationToken = default);
}
