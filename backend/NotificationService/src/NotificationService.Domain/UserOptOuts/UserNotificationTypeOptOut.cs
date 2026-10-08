using CSharpFunctionalExtensions;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Domain.UserOptOuts;

/// <summary>
/// Per-user per-type отписка от уведомлений / Per-user per-type opt-out from notifications.
///
/// Модель: присутствие строки = пользователь отписан от этого типа (ни один канал не доставит).
/// Отсутствие строки = дефолт (подписан). Доп. гранулярность поверх <c>UserNotificationChannels</c>:
/// канальный mask говорит «через какие каналы вообще», а opt-out говорит «какие типы не хочу видеть».
///
/// Пример: юзер оставил Telegram ON, но отключил <c>MaterialPublished</c> — в TG приходит
/// всё кроме публикаций материалов.
/// </summary>
public sealed class UserNotificationTypeOptOut
{
    private UserNotificationTypeOptOut(Guid userId, NotificationType type)
    {
        UserId = userId;
        Type = type;
        OptedOutAt = DateTime.UtcNow;
    }

    // EF Core
    private UserNotificationTypeOptOut()
    {
    }

    /// <summary>Идентификатор пользователя (часть composite PK).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Тип уведомления (часть composite PK).</summary>
    public NotificationType Type { get; private set; }

    /// <summary>Когда пользователь отписался (UTC).</summary>
    public DateTime OptedOutAt { get; private set; }

    public static Result<UserNotificationTypeOptOut, Error> Create(Guid userId, NotificationType type)
    {
        if (userId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("user_optout.user");

        return new UserNotificationTypeOptOut(userId, type);
    }
}
