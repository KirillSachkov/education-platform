using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Access;
using Shared.Messaging.IntegrationEvents.Auth;
using Shared.Messaging.IntegrationEvents.Notifications;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Shared.Messaging.IntegrationEvents.Telegram;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace TelegramBotService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string TELEGRAM_BOT_NOTIFICATIONS_DELIVERY_QUEUE = "telegram_bot.notifications.delivery_events";
    private const string TELEGRAM_BOT_AUTH_USER_EVENTS_QUEUE = "telegram_bot.auth.user_events";
    private const string TELEGRAM_BOT_ACCESS_GRANT_EVENTS_QUEUE = "telegram_bot.access.grant_events";
    private const string TELEGRAM_BOT_ACCESS_PLAN_DELETED_EVENTS_QUEUE = "telegram_bot.access.plan_deleted_events";

    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .UsePlatformChannelDefaults()
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(NotificationsEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AuthEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AccessEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(TelegramEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureNotificationEventsPublishing();
        opts.ConfigureTelegramEventsPublishing();
        opts.ConfigureSubscriptions();
    }

    private static void ConfigureNotificationEventsPublishing(this WolverineOptions opts)
    {
        opts.PublishMessagesToRabbitMqExchange<TelegramDeliveryRecorded>(
            NotificationsEventsRouting.EXCHANGE,
            _ => NotificationsEventsRouting.RoutingKeys.TelegramDeliveryRecorded()).UseDurableOutbox();
    }

    private static void ConfigureTelegramEventsPublishing(this WolverineOptions opts)
    {
        opts.PublishMessagesToRabbitMqExchange<ChatBindingBoundToPlan>(
            TelegramEventsRouting.EXCHANGE,
            _ => TelegramEventsRouting.RoutingKeys.ChatBindingBoundToPlan()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<ChatBindingUnboundFromPlan>(
            TelegramEventsRouting.EXCHANGE,
            _ => TelegramEventsRouting.RoutingKeys.ChatBindingUnboundFromPlan()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<ChatMemberConfirmed>(
            TelegramEventsRouting.EXCHANGE,
            _ => TelegramEventsRouting.RoutingKeys.ChatMemberConfirmed()).UseDurableOutbox();
    }

    private static void ConfigureSubscriptions(this WolverineOptions opts)
    {
        // notification.created → шлём Telegram-сообщение, если канал Telegram запрошен.
        //
        // MaximumParallelMessages = 10: на массовом fan-out (publish материала в курс с
        // 50+ подписчиков) Wolverine default'ом обрабатывал по одному сообщению, прибавляя
        // ~1s/chat throttle latency на каждом, что давало ≥50 секунд на батч (issue #67).
        // Per-chat throttler в PerChatBotThrottler (singleton, semaphore per chatId)
        // сохраняет 1s/chat ограничение Telegram Bot API даже при параллельной обработке —
        // разные chats пойдут параллельно, один и тот же chat сериализуется semaphore'ом.
        opts.ListenToRabbitQueue(TELEGRAM_BOT_NOTIFICATIONS_DELIVERY_QUEUE, queue =>
        {
            queue.BindExchange(
                NotificationsEventsRouting.EXCHANGE,
                NotificationsEventsRouting.RoutingKeys.NotificationCreated());
        }).MaximumParallelMessages(10);

        // user.telegram_unlinked → чистим локальный UserLink.
        // user.telegram_linked → re-trigger F1 invites для всех активных plan-grant'ов юзера.
        opts.ListenToRabbitQueue(TELEGRAM_BOT_AUTH_USER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AuthEventsRouting.EXCHANGE,
                AuthEventsRouting.RoutingKeys.UserTelegramUnlinked());
            queue.BindExchange(
                AuthEventsRouting.EXCHANGE,
                AuthEventsRouting.RoutingKeys.UserTelegramLinked());
        });

        // plan_grant.created/revoked/expired → F1 invite DM / F6 auto-kick (если bound к плану чат).
        opts.ListenToRabbitQueue(TELEGRAM_BOT_ACCESS_GRANT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantCreated());
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantRevoked());
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantExpired());
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantRenewalRefunded());
        });

        // plan.hard_deleted → отвязываем chat-binding'и полностью удалённого плана
        // (PlanHardDeletedTelegramHandler). Отдельная очередь от grant_events: cleanup
        // binding'ов не должен делить retry/poison-судьбу с F1/F6 grant-обработкой.
        opts.ListenToRabbitQueue(TELEGRAM_BOT_ACCESS_PLAN_DELETED_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanHardDeleted());
        });
    }
}
