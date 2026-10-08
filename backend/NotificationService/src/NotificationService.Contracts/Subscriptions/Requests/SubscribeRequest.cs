namespace NotificationService.Contracts.Subscriptions.Requests;

/// <summary>
/// Запрос на подписку / Subscribe request.
/// </summary>
public sealed record SubscribeRequest
{
    /// <summary>
    /// Тип сущности / Entity type.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Идентификатор сущности / Entity identifier.
    /// </summary>
    public required Guid EntityId { get; init; }
}
