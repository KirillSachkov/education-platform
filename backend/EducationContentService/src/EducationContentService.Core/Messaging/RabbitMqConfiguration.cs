using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Access;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Education.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace EducationContentService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string EDUCATION_CONTENT_FILE_MATERIAL_EVENTS_QUEUE = "education_content.file.material_events";
    private const string EDUCATION_CONTENT_FILE_COURSE_EVENTS_QUEUE = "education_content.file.course_events";
    private const string EDUCATION_CONTENT_FILE_COLLECTION_EVENTS_QUEUE = "education_content.file.collection_events";
    private const string EDUCATION_CONTENT_ACCESS_SYNC_QUEUE = "education_content.content_access.sync";
    private const string EDUCATION_CONTENT_ACCESS_PLAN_COURSE_EVENTS_QUEUE = "education_content.access.plan_course_events";

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
            })
            .DeclareExchange(AccessEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureEducationEventsPublishing();
        opts.PublishMessagesToRabbitMqExchange<FileAssetDetached>(
            FileEventsRouting.EXCHANGE,
            _ => FileEventsRouting.RoutingKeys.Detached()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<FileAssetBindingConfirmed>(
            FileEventsRouting.EXCHANGE,
            _ => FileEventsRouting.RoutingKeys.BindingConfirmed()).UseDurableOutbox();
        opts.ConfigureEducationEventsListeners();
        opts.ConfigureFileEventsListeners();
        opts.ConfigureAccessEventsListeners();
    }

    private static void ConfigureFileEventsListeners(this WolverineOptions opts)
    {
        opts.ListenToRabbitQueue(EDUCATION_CONTENT_FILE_MATERIAL_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE, FileEventsRouting.RoutingKeys.ALL_MATERIAL_EVENTS);
        });

        opts.ListenToRabbitQueue(EDUCATION_CONTENT_FILE_COURSE_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE, FileEventsRouting.RoutingKeys.ALL_COURSE_EVENTS);
        });

        opts.ListenToRabbitQueue(EDUCATION_CONTENT_FILE_COLLECTION_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE, FileEventsRouting.RoutingKeys.ALL_COLLECTION_EVENTS);
        });
    }

    private static void ConfigureAccessEventsListeners(this WolverineOptions opts)
    {
        opts.ListenToRabbitQueue(EDUCATION_CONTENT_ACCESS_PLAN_COURSE_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanCourseBound());
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanCourseUnbound());
        });
    }

    private static void ConfigureEducationEventsListeners(this WolverineOptions opts)
    {
        opts.ListenToRabbitQueue(EDUCATION_CONTENT_ACCESS_SYNC_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialAccessChanged());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialCreated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssueCreated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssueAccessChanged());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssueHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.CollectionCreated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.CollectionAccessChanged());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.CollectionHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.QuizPublished());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.QuizAccessChanged());
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.QuizHardDeleted());
        });
    }

    private static void ConfigureEducationEventsPublishing(this WolverineOptions opts)
    {
        string exchange = EducationEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<MaterialCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialPublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialPublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialSentToDraft>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialSentToDraft()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialArchived>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialArchived()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialAccessChanged>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialAccessChanged()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<MaterialHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<BindMaterialDraftAssets>(
            exchange, _ => EducationEventsRouting.RoutingKeys.MaterialBindDraftAssets()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueAccessChanged>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueAccessChanged()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueSoftDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueSoftDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssuePublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssuePublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueRestored>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueRestored()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<IssueHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModuleCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModuleCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModuleUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModuleUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModulePublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModulePublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModuleSoftDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModuleSoftDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModuleRestored>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModuleRestored()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ModuleHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ModuleHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ProjectCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ProjectUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ProjectPublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectPublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ProjectSoftDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectSoftDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ProjectRestored>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectRestored()).UseDurableOutbox();
        // AI-review context snapshots — consumed by AssignmentReviewService
        // (queues assignment_review.education.review_context_snapshot / review_spec).
        // Без этих двух регистраций Wolverine роняет envelope ("No routes can be
        // determined") и ARS никогда не получает project-guidelines / issue-spec'ы (#458).
        opts.PublishMessagesToRabbitMqExchange<ProjectReviewContextUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.ProjectReviewContextUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<ReviewSpecUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.IssueReviewSpecUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CoursePublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CoursePublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseSoftDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseSoftDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseRestored>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseRestored()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CourseAssetOwnershipChanged>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CourseAssetOwnershipChanged()).UseDurableOutbox();
        // Quiz access events (#490) — self-consume на content-access sync + ProgressService (ST-13).
        opts.PublishMessagesToRabbitMqExchange<QuizPublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.QuizPublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<QuizAccessChanged>(
            exchange, _ => EducationEventsRouting.RoutingKeys.QuizAccessChanged()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<QuizHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.QuizHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CollectionCreated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CollectionCreated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CollectionUpdated>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CollectionUpdated()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CollectionPublished>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CollectionPublished()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CollectionAccessChanged>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CollectionAccessChanged()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<CollectionHardDeleted>(
            exchange, _ => EducationEventsRouting.RoutingKeys.CollectionHardDeleted()).UseDurableOutbox();
        opts.PublishMessagesToRabbitMqExchange<EntityHardDeleted>(
            exchange, m => EducationEventsRouting.RoutingKeys.EntityHardDeleted(m.EntityType)).UseDurableOutbox();
    }
}
