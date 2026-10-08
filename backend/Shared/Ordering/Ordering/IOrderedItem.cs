namespace Ordering;

/// <summary>
///     Контракт для сущностей с поддержкой ordering через fractional indexing.
/// </summary>
public interface IOrderedItem
{
    SortKey SortKey { get; }

    void UpdateSortKey(SortKey sortKey);
}
