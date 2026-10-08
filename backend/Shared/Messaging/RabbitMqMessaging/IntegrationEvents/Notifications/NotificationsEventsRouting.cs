namespace Shared.Messaging.IntegrationEvents.Notifications;

public static class NotificationsEventsRouting
{
    public const string EXCHANGE = "notification.events";

    public static class RoutingKeys
    {
        public static string NotificationCreated() => "notification.created";
        public static string NotificationRead() => "notification.read";
        public static string NotificationBroadcastRequested() => "notification.broadcast_requested";

        /// <summary>
        /// Опубликован TelegramBotService после попытки доставки в Telegram. Consumer —
        /// NotificationService (пишет результат в notification_deliveries).
        /// </summary>
        public static string TelegramDeliveryRecorded() => "notification.telegram_delivery_recorded";
    }
}
