namespace NotificationService.Contracts.Inbox.Dtos;

/// <summary>
/// Ответ с количеством непрочитанных уведомлений / Unread notifications count response.
/// </summary>
public sealed record UnreadCountResponse
{
    /// <summary>
    /// Количество непрочитанных уведомлений / Unread notifications count.
    /// </summary>
    public required int Count { get; init; }
}
