using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace Ordering;

/// <summary>
///     Generic service for computing sort keys (append / move) for any ordered item.
///     Eliminates duplicated ComputeAppendSortKey / ComputeMoveSortKey across domain services.
/// </summary>
public sealed class OrderingService<T> where T : class, IOrderedItem
{
    private readonly IOrderedItemsRepository<T> _repository;

    public OrderingService(IOrderedItemsRepository<T> repository) =>
        _repository = repository;

    /// <summary>
    ///     Computes a SortKey that places a new item at the end of the scoped collection.
    /// </summary>
    public async Task<SortKey> ComputeAppendSortKeyAsync(
        Expression<Func<T, bool>> scopePredicate,
        CancellationToken cancellationToken)
    {
        Result<T, Error> lastResult = await _repository.GetByAsync(
            scopePredicate, i => i.SortKey, descending: true, cancellationToken);

        return lastResult.IsFailure
            ? SortKey.Initial()
            : SortKey.After(lastResult.Value.SortKey);
    }

    /// <summary>
    ///     Computes a SortKey that places an item between two neighbors (for move/reorder operations).
    /// </summary>
    public async Task<Result<SortKey, Error>> ComputeMoveSortKeyAsync(
        Expression<Func<T, bool>> scopePredicate,
        string? afterSortKey,
        string? beforeSortKey,
        CancellationToken cancellationToken)
    {
        // API semantics (see MoveModuleItemRequest / MoveCourseItemRequest XML docs):
        //   afterSortKey  = sort key of the item to place after  → lower bound of new position.
        //   beforeSortKey = sort key of the item to place before → upper bound of new position.
        SortKey? lower = null;
        if (afterSortKey != null)
        {
            Result<SortKey, Error> lowerResult = SortKey.Create(afterSortKey);
            if (lowerResult.IsFailure)
                return lowerResult.Error;
            lower = lowerResult.Value;
        }

        SortKey? upper = null;
        if (beforeSortKey != null)
        {
            Result<SortKey, Error> upperResult = SortKey.Create(beforeSortKey);
            if (upperResult.IsFailure)
                return upperResult.Error;
            upper = upperResult.Value;
        }

        if (lower == null && upper == null)
        {
            // Moving to the very beginning
            Result<T, Error> firstResult = await _repository.GetByAsync(
                scopePredicate, i => i.SortKey, false, cancellationToken);

            if (firstResult.IsSuccess)
                upper = firstResult.Value.SortKey;

            return upper != null
                ? SortKey.Before(upper)
                : SortKey.Initial();
        }

        if (lower != null && upper == null)
            return SortKey.After(lower);

        if (lower == null)
            return SortKey.Before(upper!);

        return SortKey.Between(before: lower, after: upper);
    }
}
