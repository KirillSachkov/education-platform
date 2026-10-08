namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на добавление алиасов к тегу / Request to add aliases to tag.
/// </summary>
public sealed record MergeTagsRequest
{
    /// <summary>
    /// Идентификаторы тегов, которые становятся алиасами / Tags that become aliases.
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
}
