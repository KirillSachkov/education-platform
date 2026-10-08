namespace Ordering;

/// <summary>
///     SQL helpers for ORDER BY with dynamic direction.
/// </summary>
public static class SqlOrdering
{
    /// <summary>
    ///     Returns "ASC" or "DESC" based on the <paramref name="ascending" /> flag.
    ///     Safe for raw-SQL interpolation — no user input is reflected.
    /// </summary>
    public static string OrderByDirection(bool ascending) =>
        ascending ? "ASC" : "DESC";
}
