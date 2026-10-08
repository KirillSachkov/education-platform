namespace NotificationService.Contracts.Inbox.Dtos;

/// <summary>
/// Данные уведомления / Notification data.
/// URL перехода считается на backend, чтобы web/email/telegram открывали один и тот же route.
/// </summary>
public sealed record NotificationDto
{
    /// <summary>Идентификатор уведомления / Notification identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Тип уведомления (значение enum NotificationType) / Notification type (NotificationType enum value).</summary>
    public required short Type { get; init; }

    /// <summary>Идентификатор шаблона / Template identifier.</summary>
    public required string TemplateId { get; init; }

    /// <summary>Заголовок уведомления (InApp-вариант) / Notification title (InApp variant).</summary>
    public required string Title { get; init; }

    /// <summary>Тело уведомления (InApp-вариант) / Notification body (InApp variant).</summary>
    public required string Body { get; init; }

    /// <summary>JSON-полезная нагрузка / JSON payload. Контракт payload зависит от Type.</summary>
    public required string Payload { get; init; }

    /// <summary>Готовый URL перехода / Resolved click target URL.</summary>
    public required string TargetUrl { get; init; }

    /// <summary>Битовая маска каналов доставки / Delivery channels bitmask.</summary>
    public required short Channels { get; init; }

    /// <summary>Дата и время создания (UTC) / Creation date and time (UTC).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Дата и время прочтения (UTC) / Read date and time (UTC).</summary>
    public DateTimeOffset? ReadAt { get; init; }

    /// <summary>Идентификатор корреляции / Correlation identifier.</summary>
    public Guid? CorrelationId { get; init; }
}
