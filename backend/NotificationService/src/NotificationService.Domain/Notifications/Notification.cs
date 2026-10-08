using CSharpFunctionalExtensions;
using SharedKernel;

namespace NotificationService.Domain.Notifications;

/// <summary>
/// Агрегат уведомления / Notification aggregate root.
///
/// Фронт маршрутизирует клик по уведомлению на основе <see cref="Type"/> + <see cref="Payload"/>,
/// поэтому отдельного <c>DeepLink</c> в модели нет — это сохраняет бэкенд independent от URL-схемы
/// фронта и позволяет переписывать маршруты без миграций БД.
/// </summary>
public sealed class Notification
{
    /// <summary>
    /// Максимальная длина идентификатора шаблона / Maximum template identifier length.
    /// </summary>
    public const int TEMPLATE_ID_MAX_LENGTH = 64;

    private Notification(
        NotificationId id,
        Guid recipientUserId,
        NotificationType type,
        string templateId,
        string title,
        string body,
        NotificationChannel channels,
        string payload,
        Guid? correlationId)
    {
        Id = id;
        RecipientUserId = recipientUserId;
        Type = type;
        TemplateId = templateId;
        Title = title;
        Body = body;
        Channels = channels;
        Payload = payload;
        CorrelationId = correlationId;
        CreatedAt = DateTime.UtcNow;
        ReadAt = null;
    }

    // EF Core
    private Notification()
    {
    }

    /// <summary>Идентификатор уведомления / Notification identifier.</summary>
    public NotificationId Id { get; private set; } = null!;

    /// <summary>Получатель уведомления / Notification recipient.</summary>
    public Guid RecipientUserId { get; private set; }

    /// <summary>Тип уведомления / Notification type.</summary>
    public NotificationType Type { get; private set; }

    /// <summary>Идентификатор шаблона / Template identifier.</summary>
    public string TemplateId { get; private set; } = null!;

    /// <summary>Заголовок (InApp-вариант, для drawer/колокольчика) / Title (InApp variant).</summary>
    public string Title { get; private set; } = null!;

    /// <summary>Тело (InApp-вариант) / Body (InApp variant).</summary>
    public string Body { get; private set; } = null!;

    /// <summary>Каналы доставки (битовая маска) / Delivery channels (bitmask).</summary>
    public NotificationChannel Channels { get; private set; }

    /// <summary>Полезная нагрузка в формате JSON / JSON payload. Фронт маршрутизирует по (Type + Payload).</summary>
    public string Payload { get; private set; } = "{}";

    /// <summary>Идентификатор корреляции для идемпотентности / Correlation identifier for idempotency.</summary>
    public Guid? CorrelationId { get; private set; }

    /// <summary>Дата и время создания (UTC) / Creation date and time (UTC).</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>Дата и время прочтения (UTC) / Read date and time (UTC).</summary>
    public DateTime? ReadAt { get; private set; }

    /// <summary>
    /// Создаёт новое уведомление / Creates a new notification.
    /// </summary>
    public static Result<Notification, Error> Create(
        Guid recipientUserId,
        NotificationType type,
        string templateId,
        string title,
        string body,
        NotificationChannel channels,
        string payload,
        Guid? correlationId,
        NotificationId? id = null)
    {
        if (recipientUserId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("notification.recipient");

        if (!Enum.IsDefined(type))
            return GeneralErrors.ValueIsInvalid("notification.type");

        if (string.IsNullOrWhiteSpace(templateId))
            return GeneralErrors.ValueIsRequired("notification.template");

        if (templateId.Length > TEMPLATE_ID_MAX_LENGTH)
            return GeneralErrors.ValueIsInvalid("notification.template");

        if (string.IsNullOrWhiteSpace(title))
            return GeneralErrors.ValueIsRequired("notification.title");

        if (string.IsNullOrWhiteSpace(body))
            return GeneralErrors.ValueIsRequired("notification.body");

        string normalizedPayload = string.IsNullOrWhiteSpace(payload) ? "{}" : payload;

        return new Notification(
            id ?? NotificationId.Create(),
            recipientUserId,
            type,
            templateId,
            title,
            body,
            channels,
            normalizedPayload,
            correlationId);
    }

    /// <summary>
    /// Помечает уведомление как прочитанное / Marks the notification as read.
    /// </summary>
    /// <returns>true — если помечено сейчас; false — если уже было прочитано.</returns>
    public bool MarkAsRead()
    {
        if (ReadAt.HasValue)
            return false;

        ReadAt = DateTime.UtcNow;
        return true;
    }
}
