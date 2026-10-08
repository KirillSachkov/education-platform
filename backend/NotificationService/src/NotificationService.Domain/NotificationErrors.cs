using SharedKernel;

namespace NotificationService.Domain;

public static class NotificationErrors
{
    public static Error NotificationAlreadyExists() =>
        Error.Conflict("notification.already.exists", "Уведомление уже создано (duplicate по correlation_id)");

    public static Error InvalidSubscriptionEntityType(string value) =>
        Error.Validation("subscription.entity.type.invalid", $"Недопустимый тип подписки: {value}");

    public static Error SubscriptionAlreadyExists() =>
        Error.Conflict("subscription.already.exists", "Подписка уже существует");

    public static Error PreferenceNotFound(Guid userId, int type) =>
        Error.NotFound("preference.not.found", $"Настройка уведомления для пользователя {userId}, тип {type} не найдена");

    /// <summary>
    /// Гонка двух concurrent retry'ев одного и того же event'a (например
    /// <c>notification.telegram_delivery_recorded</c>) — оба попали в окно «нет
    /// записи» и оба сделали INSERT. Unique constraint
    /// <c>ux_notification_deliveries_notification_channel</c> блокирует второй
    /// INSERT — handler ловит этот код и переходит в UPDATE-ветку.
    /// </summary>
    public static Error DeliveryAlreadyExists() =>
        Error.Conflict("delivery.already.exists", "Запись о доставке уже существует");
}
