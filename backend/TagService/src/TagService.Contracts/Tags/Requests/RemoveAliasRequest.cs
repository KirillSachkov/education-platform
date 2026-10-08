namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на удаление алиасов у тега / Request to remove aliases from a tag.
/// </summary>
public sealed record RemoveAliasRequest
{
    /// <summary>
    /// Идентификаторы тегов-алиасов / Alias tag identifiers.
    /// </summary>
    public IReadOnlyList<Guid> TagIds { get; init; } = [];
}
