using System.Linq.Expressions;
using AuthService.Domain.AuthorSpaces;

namespace AuthService.Core.Database;

public interface IAuthorSpaceRepository
{
    Task<AuthorSpace?> GetByAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct);

    Task<List<AuthorSpace>> GetManyByAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct);

    Task<bool> ExistsAsync(
        Expression<Func<AuthorSpace, bool>> predicate,
        CancellationToken ct);

    Task AddAsync(AuthorSpace space, CancellationToken ct);
}
