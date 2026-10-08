namespace NotificationService.Contracts.Inbox.Dtos;

/// <summary>
/// Ответ со списком уведомлений и курсором следующей страницы /
/// Notifications list response with next-page cursor.
/// </summary>
public sealed record NotificationListResponse
{
    /// <summary>
    /// Элементы списка / List items.
    /// </summary>
    public required IReadOnlyList<NotificationDto> Items { get; init; }

    /// <summary>
    /// Курсор для следующей страницы: временная метка / Next-page cursor: timestamp.
    /// </summary>
    public DateTimeOffset? NextCursorBefore { get; init; }

    /// <summary>
    /// Курсор для следующей страницы: идентификатор / Next-page cursor: identifier.
    /// </summary>
    public Guid? NextCursorId { get; init; }
}
