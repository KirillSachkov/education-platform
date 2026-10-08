using System.Linq.Expressions;
using ServiceName.Domain.Widgets;

namespace ServiceName.Persistence.Repositories;

/// <summary>
/// Platform repository convention: Expression-based filters in handlers,
/// specialized methods only when encapsulating a domain rule or a bulk operation.
/// </summary>
public interface IWidgetsRepository
{
    Task AddAsync(Widget widget, CancellationToken ct = default);

    Task<Result<Widget, Error>> GetByAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Widget>> GetManyByAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<Widget, bool>> predicate,
        CancellationToken ct = default);
}
