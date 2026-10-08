namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на получение популярных тегов / Request to get popular tags.
/// </summary>
public sealed record GetPopularTagsRequest
{
    /// <summary>
    /// Фильтр по автору / Filter by author.
    /// </summary>
    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Максимальное число тегов / Maximum number of tags.
    /// </summary>
    public int Limit { get; init; } = 10;
}
