namespace NotificationService.Contracts.Inbox.Requests;

/// <summary>
/// Запрос списка уведомлений / Request for notifications list.
/// </summary>
public sealed record GetNotificationsRequest
{
    /// <summary>
    /// Размер страницы (1..100) / Page size (1..100).
    /// </summary>
    public int Limit { get; init; } = 20;

    /// <summary>
    /// Курсор: вернуть уведомления, созданные ранее указанного момента /
    /// Cursor: return notifications created before the given timestamp.
    /// </summary>
    public DateTime? CursorBefore { get; init; }

    /// <summary>
    /// Курсор: идентификатор последнего уведомления предыдущей страницы /
    /// Cursor: identifier of the last notification on the previous page.
    /// </summary>
    public Guid? CursorId { get; init; }

    /// <summary>
    /// Фильтр «только непрочитанные» / Unread-only filter.
    /// </summary>
    public bool UnreadOnly { get; init; }

    /// <summary>
    /// Фильтр по типам нотификаций (short-коды <c>NotificationType</c>). Пустой / null — все типы.
    /// Передаётся в query string как повторяющийся параметр: <c>?types=10&amp;types=11</c>.
    /// Используется для server-side фильтрации в инбоксе (вкладка «Комментарии», «Задания», и т.д.).
    /// </summary>
    public IReadOnlyList<short>? Types { get; init; }
}
