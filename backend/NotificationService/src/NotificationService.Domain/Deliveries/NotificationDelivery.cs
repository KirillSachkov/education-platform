using CSharpFunctionalExtensions;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Domain.Deliveries;

/// <summary>
/// Запись о попытке доставки уведомления по конкретному каналу / Delivery attempt record for a single channel.
/// </summary>
public sealed class NotificationDelivery
{
    /// <summary>
    /// Максимальная длина идентификатора сообщения провайдера / Maximum provider message identifier length.
    /// </summary>
    public const int PROVIDER_MESSAGE_ID_MAX_LENGTH = 128;

    /// <summary>
    /// Максимальная длина кода ошибки / Maximum error code length.
    /// </summary>
    public const int ERROR_CODE_MAX_LENGTH = 64;

    private NotificationDelivery(
        NotificationDeliveryId id,
        NotificationId notificationId,
        NotificationChannel channel)
    {
        Id = id;
        NotificationId = notificationId;
        Channel = channel;
        Status = DeliveryStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    // EF Core
    private NotificationDelivery()
    {
    }

    /// <summary>
    /// Идентификатор записи / Record identifier.
    /// </summary>
    public NotificationDeliveryId Id { get; private set; } = null!;

    /// <summary>
    /// Идентификатор уведомления / Notification identifier.
    /// </summary>
    public NotificationId NotificationId { get; private set; } = null!;

    /// <summary>
    /// Канал доставки (одно значение) / Delivery channel (single value).
    /// </summary>
    public NotificationChannel Channel { get; private set; }

    /// <summary>
    /// Статус доставки / Delivery status.
    /// </summary>
    public DeliveryStatus Status { get; private set; }

    /// <summary>
    /// Идентификатор сообщения во внешнем провайдере / Provider message identifier.
    /// </summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>
    /// Код ошибки / Error code.
    /// </summary>
    public string? ErrorCode { get; private set; }

    /// <summary>
    /// Детали ошибки / Error detail.
    /// </summary>
    public string? ErrorDetail { get; private set; }

    /// <summary>
    /// Дата и время создания / Creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Дата и время завершения попытки / Completion date and time.
    /// </summary>
    public DateTime? CompletedAt { get; private set; }

    /// <summary>
    /// Создаёт новую запись о доставке со статусом Pending / Creates a new delivery record in Pending state.
    /// </summary>
    public static Result<NotificationDelivery, Error> Create(
        NotificationId notificationId,
        NotificationChannel channel)
    {
        if (notificationId is null)
            return GeneralErrors.ValueIsRequired("delivery.notification");

        if (channel == NotificationChannel.None)
            return GeneralErrors.ValueIsInvalid("delivery.channel");

        return new NotificationDelivery(NotificationDeliveryId.Create(), notificationId, channel);
    }

    /// <summary>
    /// Помечает доставку как успешную / Marks the delivery as successful.
    /// </summary>
    public void MarkDelivered(string? providerMessageId)
    {
        Status = DeliveryStatus.Delivered;
        ProviderMessageId = providerMessageId;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Помечает доставку как неуспешную / Marks the delivery as failed.
    /// </summary>
    public void MarkFailed(string errorCode, string errorDetail)
    {
        Status = DeliveryStatus.Failed;
        ErrorCode = errorCode;
        ErrorDetail = errorDetail;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Помечает доставку как пропущенную / Marks the delivery as skipped.
    /// <c>errorCode</c> — стабильный код пропуска (например <c>no_user_link</c>),
    /// <c>errorDetail</c> — необязательный читаемый текст. Раньше вызов
    /// <c>MarkSkipped(reason)</c> писал reason в <c>error_detail</c> и оставлял
    /// <c>error_code = NULL</c>, что ломало <c>GROUP BY error_code</c> для метрик.
    /// </summary>
    public void MarkSkipped(string errorCode, string? errorDetail = null)
    {
        Status = DeliveryStatus.Skipped;
        ErrorCode = errorCode;
        ErrorDetail = errorDetail;
        CompletedAt = DateTime.UtcNow;
    }
}
