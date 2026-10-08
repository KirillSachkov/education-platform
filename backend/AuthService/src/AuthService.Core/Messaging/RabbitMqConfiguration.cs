using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Auth;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace AuthService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string AUTH_FILE_USER_EVENTS_QUEUE = "auth.file.user_events";

    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .UsePlatformChannelDefaults()
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(AuthEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(FileEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureAuthEventsPublishing();
        opts.PublishMessagesToRabbitMqExchange<FileAssetDetached>(
            FileEventsRouting.EXCHANGE,
            _ => FileEventsRouting.RoutingKeys.Detached()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<FileAssetBindingConfirmed>(
            FileEventsRouting.EXCHANGE,
            _ => FileEventsRouting.RoutingKeys.BindingConfirmed()).UseDurableOutbox();
        opts.ConfigureFileEventsListeners();
    }

    private static void ConfigureAuthEventsPublishing(this WolverineOptions opts)
    {
        string exchange = AuthEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<UserCreated>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserCreated()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserLoggedIn>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserLoggedIn()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserUsernameUpdated>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserUsernameUpdated()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserDisplayNameUpdated>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserDisplayNameUpdated()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserAvatarUpdated>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserAvatarUpdated()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserGithubLogin>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserGithubLogin()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserTelegramLinked>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserTelegramLinked()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<UserTelegramUnlinked>(
            exchange,
            _ => AuthEventsRouting.RoutingKeys.UserTelegramUnlinked()).UseDurableOutbox();
    }
    
    private static void ConfigureFileEventsListeners(this WolverineOptions opts)
    {
        opts.ListenToRabbitQueue(AUTH_FILE_USER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE, "file.bound.user");
            queue.BindExchange(FileEventsRouting.EXCHANGE, "file.deleted.user");
        });
    }
}
