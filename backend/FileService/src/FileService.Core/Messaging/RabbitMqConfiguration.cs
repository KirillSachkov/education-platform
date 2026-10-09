using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace FileService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string FILE_EDUCATION_ASSET_CLEANUP_EVENTS_QUEUE = "file.education.asset_cleanup_events";
    private const string FILE_ASSET_DETACHED_QUEUE = "file.asset_detached";
    private const string FILE_EDUCATION_BIND_DRAFT_ASSETS_QUEUE = "file.education.bind_draft_assets";
    private const string FILE_EDUCATION_ASSET_OWNERSHIP_QUEUE = "file.education.asset_ownership";

    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .UsePlatformChannelDefaults()
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(EducationEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(FileEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureFileEventsPublishing();
        opts.ConfigureEducationEventsListeners();
    }

    private static void ConfigureEducationEventsListeners(this WolverineOptions opts)
    {
        // Hard-delete events (course/module/lesson/issue/entity hard-deleted) drive asset
        // cleanup through AssetDeletionLifecycleService. Error handling for this queue is
        // governed by the shared ConfigureStandardErrorPolicies() policy:
        //
        //   * Transient failures (NpgsqlException.IsTransient, HttpRequestException,
        //     TimeoutException, IOException): in-memory cooldown retries, then two
        //     scheduled retries at 3s and 10-15s — this covers S3/RabbitMq/DB blips
        //     long enough for most outages to recover without operator intervention.
        //   * The catch-all Exception policy retries with cooldown and then at 5s
        //     before moving the message to the .error DLQ.
        //
        // This means an S3 outage mid-batch will retry up to ~20s and then dead-letter
        // the hard-delete event — assets may temporarily remain in their prior state
        // until the message is replayed from the DLQ. Operators must monitor the
        // .error queue for file.education.asset_cleanup_events after prolonged S3
        // outages and replay stuck messages so that orphaned assets are eventually
        // reconciled.
        opts.ListenToRabbitQueue(FILE_EDUCATION_ASSET_CLEANUP_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.ALL_HARD_DELETED);
        });

        opts.ListenToRabbitQueue(FILE_ASSET_DETACHED_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE, FileEventsRouting.RoutingKeys.Detached());
            queue.BindExchange(FileEventsRouting.EXCHANGE, FileEventsRouting.RoutingKeys.BindingConfirmed());
        });

        opts.ListenToRabbitQueue(FILE_EDUCATION_BIND_DRAFT_ASSETS_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialBindDraftAssets());
        });

        opts.ListenToRabbitQueue(FILE_EDUCATION_ASSET_OWNERSHIP_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.CourseAssetOwnershipChanged());
        });
    }

    private static void ConfigureFileEventsPublishing(this WolverineOptions opts)
    {
        opts.PublishMessagesToRabbitMqExchange<FileAssetBound>(
            FileEventsRouting.EXCHANGE,
            m => FileEventsRouting.RoutingKeys.Bound(m.TargetEntityType)).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<FileAssetDeleted>(
            FileEventsRouting.EXCHANGE,
            m => FileEventsRouting.RoutingKeys.Deleted(m.TargetEntityType ?? "unbound")).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<VideoUploadInitiated>(
            FileEventsRouting.EXCHANGE,
            m => FileEventsRouting.RoutingKeys.UploadInitiated(m.TargetEntityType)).UseDurableOutbox();

    }
}
