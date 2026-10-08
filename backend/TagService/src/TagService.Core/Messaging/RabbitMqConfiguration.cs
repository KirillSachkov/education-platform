using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Tags;
using Shared.Messaging.IntegrationEvents.Tags.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace TagService.Core.Messaging;

public static class RabbitMqConfiguration
{
    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .UsePlatformChannelDefaults()
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(TagEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureTagEventsPublishing();
    }

    private static void ConfigureTagEventsPublishing(this WolverineOptions opts)
    {
        string exchange = TagEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<TagsAddedToEntity>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagsAddedToEntity()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<TagsRemovedFromEntity>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagsRemovedFromEntity()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<TagsDeleted>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagsDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<TagsMerged>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagsMerged()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<TagUpdated>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagsUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<TagAliasRemoved>(
            exchange,
            _ => TagEventsRouting.RoutingKeys.TagAliasRemoved()).UseDurableOutbox();
    }
}
