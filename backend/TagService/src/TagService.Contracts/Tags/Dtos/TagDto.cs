namespace TagService.Contracts.Tags.Dtos;

/// <summary>
/// Данные тега / Tag data.
/// </summary>
public sealed record TagDto
{
    /// <summary>
    /// Идентификатор тега / Tag identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Заголовок тега / Tag title.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Slug тега / Tag slug.
    /// </summary>
    public required string Slug { get; init; }

    /// <summary>
    /// Тип тега / Tag kind.
    /// </summary>
    public required string Kind { get; init; }
}
