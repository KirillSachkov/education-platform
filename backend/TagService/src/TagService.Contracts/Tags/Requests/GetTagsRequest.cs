namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на получение списка тегов с курсорной пагинацией / Request to get tags list with cursor pagination.
/// </summary>
public sealed record GetTagsRequest
{
    /// <summary>
    /// Поисковая строка / Search query.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Фильтр по типу тега / Filter by tag kind.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Фильтр по автору / Filter by author.
    /// </summary>
    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Курсор следующей страницы (null для первой) / Cursor for next page (null for first).
    /// </summary>
    public string? Cursor { get; init; }

    /// <summary>
    /// Размер страницы / Page size.
    /// </summary>
    public int Limit { get; init; } = 20;
}
