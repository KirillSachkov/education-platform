using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Comments;
using Shared.Messaging.IntegrationEvents.Comments.Events;
using Shared.Messaging.IntegrationEvents.Education;
using Wolverine;
using Wolverine.RabbitMQ;

namespace CommentService.Core.Messaging;

public static class RabbitMqConfiguration
{
    public const string COMMENTS_EDUCATION_LIFECYCLE_EVENTS_QUEUE = "comments.education.lifecycle_events";

    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .UsePlatformChannelDefaults()
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(CommentEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(EducationEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureCommentEventsPublishing();
        opts.ConfigureEducationEventsListeners();
    }

    private static void ConfigureCommentEventsPublishing(this WolverineOptions opts)
    {
        string exchange = CommentEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<CommentCreated>(
            exchange,
            _ => CommentEventsRouting.RoutingKeys.CommentCreated()).UseDurableOutbox();
    }

    private static void ConfigureEducationEventsListeners(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(COMMENTS_EDUCATION_LIFECYCLE_EVENTS_QUEUE, queue =>
        {
            // Только hard-delete события — комментарии цепляются к target'у, который пропадает
            // навсегда. Soft-delete / publish / archive не требуют каскада.
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.MaterialHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.CourseHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.IssueHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.QuizHardDeleted());
        });
}
