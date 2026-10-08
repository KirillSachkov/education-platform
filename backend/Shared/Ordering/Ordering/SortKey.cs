using CSharpFunctionalExtensions;
using SharedKernel;

namespace Ordering;

/// <summary>
///     Value object representing a lexicographically sortable position key.
///     Uses string-based fractional indexing (base-62) for O(1) insertions
///     without renormalization.
/// </summary>
public sealed record SortKey
{
    private SortKey(string value) => Value = value;

    /// <summary>Gets the raw string key value.</summary>
    public string Value { get; }

    /// <summary>
    ///     Creates a <see cref="SortKey" /> from an existing string, validating its format.
    /// </summary>
    /// <param name="value">The raw order key string.</param>
    public static Result<SortKey, Error> Create(string value)
    {
        if (!FractionalIndexing.IsValidOrderKey(value))
        {
            return OrderingErrors.InvalidSortKey($"'{value}'");
        }

        return new SortKey(value);
    }

    /// <summary>
    ///     Generates the initial key ("a0"). Use when the scope has no items.
    /// </summary>
    public static SortKey Initial() =>
        new(FractionalIndexing.GenerateKeyBetween(null, null));

    /// <summary>
    ///     Generates a key after <paramref name="previous" />. Use for appending to the end.
    /// </summary>
    /// <param name="previous">The current last key in scope.</param>
    public static SortKey After(SortKey previous) =>
        new(FractionalIndexing.GenerateKeyBetween(previous.Value, null));

    /// <summary>
    ///     Generates a key before <paramref name="next" />. Use for prepending to the start.
    /// </summary>
    /// <param name="next">The current first key in scope.</param>
    public static SortKey Before(SortKey next) =>
        new(FractionalIndexing.GenerateKeyBetween(null, next.Value));

    /// <summary>
    ///     Generates a key between two existing keys.
    /// </summary>
    /// <param name="before">Lower bound (or null for no lower bound).</param>
    /// <param name="after">Upper bound (or null for no upper bound).</param>
    public static Result<SortKey, Error> Between(SortKey? before, SortKey? after)
    {
        try
        {
            string key = FractionalIndexing.GenerateKeyBetween(before?.Value, after?.Value);
            return new SortKey(key);
        }
        catch (ArgumentException)
        {
            return OrderingErrors.KeyConflict();
        }
    }

    /// <summary>
    ///     Generates <paramref name="count" /> evenly-spaced keys between two bounds.
    /// </summary>
    /// <param name="before">Lower bound (or null for no lower bound).</param>
    /// <param name="after">Upper bound (or null for no upper bound).</param>
    /// <param name="count">Number of keys to generate.</param>
    public static SortKey[] CreateBatch(SortKey? before, SortKey? after, int count)
    {
        string[] keys = FractionalIndexing.GenerateNKeysBetween(before?.Value, after?.Value, count);
        return keys.Select(k => new SortKey(k)).ToArray();
    }

    public override string ToString() => Value;
}