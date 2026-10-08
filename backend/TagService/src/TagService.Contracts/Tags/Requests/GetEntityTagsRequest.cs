namespace TagService.Contracts.Tags.Requests;

/// <summary>
/// Запрос на получение тегов сущности / Request to get tags of an entity.
/// </summary>
public sealed record GetEntityTagsRequest
{
    /// <summary>
    /// Тип сущности / Entity type.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Идентификатор сущности / Entity identifier.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Номер страницы / Page number.
    /// </summary>
    public int Page { get; init; } = 1;

    /// <summary>
    /// Размер страницы / Page size.
    /// </summary>
    public int PageSize { get; init; } = 20;
}
