namespace NotificationService.Contracts.Subscriptions.Dtos;

/// <summary>
/// Ответ со списком подписок / Subscriptions list response.
/// </summary>
public sealed record SubscriptionsListResponse
{
    /// <summary>
    /// Элементы списка / List items.
    /// </summary>
    public required IReadOnlyList<SubscriptionDto> Items { get; init; }
}
