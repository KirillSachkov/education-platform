namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на подсказку тегов / Request for tag suggestions.
/// </summary>
public sealed record SuggestTagsRequest
{
    /// <summary>
    /// Поисковая строка / Search query.
    /// </summary>
    public string? Search { get; init; }

    /// <summary>
    /// Фильтр по автору / Filter by author.
    /// </summary>
    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Номер страницы / Page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Размер страницы / Page size.
    /// </summary>
    public int PageSize { get; init; } = 20;
}
