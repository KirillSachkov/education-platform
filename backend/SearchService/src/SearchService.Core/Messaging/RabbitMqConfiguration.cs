using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Tags;
using Wolverine;
using Wolverine.RabbitMQ;

namespace SearchService.Core.Messaging;

public static class RabbitMqConfiguration
{
    public const string SEARCH_EDUCATION_LIFECYCLE_EVENTS_QUEUE = "search.education.lifecycle_events";
    public const string SEARCH_TAG_EVENTS_QUEUE = "search.tag.events";

    public static IReadOnlyList<string> SearchIndexingQueueNames { get; } =
    [
        SEARCH_EDUCATION_LIFECYCLE_EVENTS_QUEUE,
        SEARCH_TAG_EVENTS_QUEUE,
    ];

    public static IReadOnlyList<string> EducationLifecycleRoutingKeys { get; } =
    [
        EducationEventsRouting.RoutingKeys.CourseCreated(),
        EducationEventsRouting.RoutingKeys.CourseUpdated(),
        EducationEventsRouting.RoutingKeys.CoursePublished(),
        EducationEventsRouting.RoutingKeys.CourseSoftDeleted(),
        EducationEventsRouting.RoutingKeys.CourseRestored(),
        EducationEventsRouting.RoutingKeys.CourseHardDeleted(),
        EducationEventsRouting.RoutingKeys.ModuleCreated(),
        EducationEventsRouting.RoutingKeys.ModuleUpdated(),
        EducationEventsRouting.RoutingKeys.ModulePublished(),
        EducationEventsRouting.RoutingKeys.ModuleSoftDeleted(),
        EducationEventsRouting.RoutingKeys.ModuleRestored(),
        EducationEventsRouting.RoutingKeys.ModuleHardDeleted(),
        EducationEventsRouting.RoutingKeys.MaterialCreated(),
        EducationEventsRouting.RoutingKeys.MaterialUpdated(),
        EducationEventsRouting.RoutingKeys.MaterialPublished(),
        EducationEventsRouting.RoutingKeys.MaterialArchived(),
        EducationEventsRouting.RoutingKeys.MaterialSentToDraft(),
        EducationEventsRouting.RoutingKeys.MaterialAccessChanged(),
        EducationEventsRouting.RoutingKeys.MaterialHardDeleted(),
        EducationEventsRouting.RoutingKeys.ProjectCreated(),
        EducationEventsRouting.RoutingKeys.ProjectUpdated(),
        EducationEventsRouting.RoutingKeys.ProjectPublished(),
        EducationEventsRouting.RoutingKeys.ProjectSoftDeleted(),
        EducationEventsRouting.RoutingKeys.ProjectRestored(),
        EducationEventsRouting.RoutingKeys.IssueCreated(),
        EducationEventsRouting.RoutingKeys.IssueUpdated(),
        EducationEventsRouting.RoutingKeys.IssueAccessChanged(),
        EducationEventsRouting.RoutingKeys.IssuePublished(),
        EducationEventsRouting.RoutingKeys.IssueSoftDeleted(),
        EducationEventsRouting.RoutingKeys.IssueRestored(),
        EducationEventsRouting.RoutingKeys.IssueHardDeleted(),
        EducationEventsRouting.RoutingKeys.CollectionCreated(),
        EducationEventsRouting.RoutingKeys.CollectionUpdated(),
        EducationEventsRouting.RoutingKeys.CollectionPublished(),
        EducationEventsRouting.RoutingKeys.CollectionAccessChanged(),
        EducationEventsRouting.RoutingKeys.CollectionHardDeleted(),
    ];

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
            .DeclareExchange(TagEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureEducationEventsListeners();
        opts.ConfigureTagEventsListeners();
    }

    private static void ConfigureEducationEventsListeners(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(SEARCH_EDUCATION_LIFECYCLE_EVENTS_QUEUE, queue =>
        {
            foreach (string routingKey in EducationLifecycleRoutingKeys)
            {
                queue.BindExchange(EducationEventsRouting.EXCHANGE, routingKey);
            }
        });

    private static void ConfigureTagEventsListeners(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(SEARCH_TAG_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagsAddedToEntity());
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagsRemovedFromEntity());
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagsDeleted());
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagsMerged());
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagsUpdated());
            queue.BindExchange(TagEventsRouting.EXCHANGE, TagEventsRouting.RoutingKeys.TagAliasRemoved());
        });
}
