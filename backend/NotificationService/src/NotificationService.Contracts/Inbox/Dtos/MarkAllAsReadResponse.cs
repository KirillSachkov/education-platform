namespace NotificationService.Contracts.Inbox.Dtos;

/// <summary>
/// Ответ на пометку всех уведомлений прочитанными / Mark-all-as-read response.
/// </summary>
public sealed record MarkAllAsReadResponse
{
    /// <summary>
    /// Количество обновлённых записей / Number of updated rows.
    /// </summary>
    public required int Updated { get; init; }
}
