using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace Ordering;

/// <summary>
///     Generic repository contract for ordered items.
///     Concrete repository interfaces extend this and add domain-specific methods.
/// </summary>
public interface IOrderedItemsRepository<T> where T : class, IOrderedItem
{
    Task AddAsync(T item, CancellationToken cancellationToken = default);

    Task<Result<T, Error>> GetByAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    Task<List<T>> GetManyByAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default);

    void Delete(T item);
}
