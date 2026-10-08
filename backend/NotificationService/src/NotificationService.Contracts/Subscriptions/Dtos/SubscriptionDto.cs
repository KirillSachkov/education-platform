namespace NotificationService.Contracts.Subscriptions.Dtos;

/// <summary>
/// Данные подписки пользователя / User subscription data.
/// </summary>
public sealed record SubscriptionDto
{
    /// <summary>
    /// Идентификатор подписки / Subscription identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Тип сущности / Entity type.
    /// </summary>
    public required string EntityType { get; init; }

    /// <summary>
    /// Идентификатор сущности / Entity identifier.
    /// </summary>
    public required Guid EntityId { get; init; }

    /// <summary>
    /// Название сущности — title курса / модуля или имя автора.
    /// Null если lookup упал (graceful fallback на UI).
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Дата и время создания / Creation date and time.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
