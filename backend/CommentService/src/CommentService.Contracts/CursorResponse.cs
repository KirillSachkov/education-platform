namespace CommentService.Contracts;

/// <summary>
/// Ответ с курсорной пагинацией / Cursor-based pagination response.
/// </summary>
/// <typeparam name="T">Тип элементов в коллекции / Type of items in the collection.</typeparam>
public record CursorResponse<T>
{
    /// <summary>
    /// Список элементов текущей страницы / List of items for the current page.
    /// </summary>
    public required IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Курсор для получения следующей страницы / Cursor for fetching the next page.
    /// </summary>
    public required string? NextCursor { get; init; }

    /// <summary>
    /// Общее количество элементов / Total count of items.
    /// </summary>
    public required long TotalCount { get; init; }
}