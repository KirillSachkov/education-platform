namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на создание тега / Request to create a tag.
/// </summary>
public sealed record CreateTagRequest
{
    /// <summary>
    /// Название тега / Tag title.
    /// </summary>
    public required string Title { get; init; }
}
