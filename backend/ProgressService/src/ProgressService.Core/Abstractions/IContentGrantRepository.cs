using System.Linq.Expressions;
using ProgressService.Domain.ContentAccess;

namespace ProgressService.Core.Abstractions;

public interface IContentGrantRepository
{
    Task<ContentGrant?> GetByAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<ContentGrant>> GetManyByAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default);

    Task AddAsync(ContentGrant grant, CancellationToken ct = default);

    Task AddRangeAsync(IReadOnlyCollection<ContentGrant> grants, CancellationToken ct = default);
}
