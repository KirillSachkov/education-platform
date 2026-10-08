namespace TagService.Contracts.Tags;

/// <summary>
/// Ответ с курсорной пагинацией / Cursor-based pagination response.
/// </summary>
public sealed record CursorResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Курсор для следующей страницы (null если больше нет) / Cursor for the next page (null when exhausted).
    /// </summary>
    public required string? NextCursor { get; init; }

    /// <summary>
    /// Общее число записей под текущим фильтром / Total record count under the current filter.
    /// </summary>
    public required long TotalCount { get; init; }
}
