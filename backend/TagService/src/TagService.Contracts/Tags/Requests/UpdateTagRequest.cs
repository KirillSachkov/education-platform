namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на обновление тега / Request to update a tag.
/// </summary>
public sealed record UpdateTagRequest
{
    /// <summary>
    /// Новое название тега / New tag title.
    /// </summary>
    public required string Title { get; init; }
}
