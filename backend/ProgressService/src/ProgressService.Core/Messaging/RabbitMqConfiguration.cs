using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Progress;
using Shared.Messaging.IntegrationEvents.Progress.Events;
using Wolverine;
using Wolverine.RabbitMQ;

namespace ProgressService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string PROGRESS_EDUCATION_LIFECYCLE_EVENTS_QUEUE = "progress.education.lifecycle_events";
    private const string PROGRESS_ASSIGNMENT_REVIEW_DENORM_QUEUE = "progress.assignment_review.denorm";

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
            .DeclareExchange(ProgressEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AssignmentReviewEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureEducationEventsListeners();
        opts.ConfigureAssignmentReviewListeners();
        opts.ConfigureProgressEventsPublishing();
    }

    private static void ConfigureAssignmentReviewListeners(this WolverineOptions opts) =>
        // Phase 8 (#15): denorm AI fields на issue_submissions + gate
        // ReadyForHumanReview через AiReviewQueuedForSubmission.
        // #713: student_pr_question.asked — денорм времени вопроса студента в PR
        // (бейдж «новый вопрос от студента» в панели «Проверка работ»).
        opts.ListenToRabbitQueue(PROGRESS_ASSIGNMENT_REVIEW_DENORM_QUEUE, queue =>
        {
            queue.BindExchange(AssignmentReviewEventsRouting.EXCHANGE, AssignmentReviewEventsRouting.RoutingKeys.IterationCompleted());
            queue.BindExchange(AssignmentReviewEventsRouting.EXCHANGE, AssignmentReviewEventsRouting.RoutingKeys.QueuedForSubmission());
            queue.BindExchange(AssignmentReviewEventsRouting.EXCHANGE, AssignmentReviewEventsRouting.RoutingKeys.StudentPrQuestionAsked());
        });

    private static void ConfigureEducationEventsListeners(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(PROGRESS_EDUCATION_LIFECYCLE_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.CourseCreated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.MaterialHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.ModuleHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.IssueHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.CourseHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.QuizHardDeleted());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.IssuePublished());
        });

    private static void ConfigureProgressEventsPublishing(this WolverineOptions opts)
    {
        string exchange = ProgressEventsRouting.EXCHANGE;

        // access-derive-model Phase 4: CourseEnrolled / CourseEnrollmentRevoked /
        // CourseEnrollmentRestored сняты. Enrollment'ы — lazy progress-anchor'ы без
        // cross-service сигнала; доступ, уведомления и подписки целиком grant-driven
        // (AccessService plan-grants + NotificationService на plan_grant.created).
        opts.PublishMessagesToRabbitMqExchange<IssueSubmissionApproved>(
            exchange, _ => ProgressEventsRouting.RoutingKeys.IssueSubmissionApproved()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<IssueSubmissionChangesRequested>(
            exchange, _ => ProgressEventsRouting.RoutingKeys.IssueSubmissionChangesRequested()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<IssueSubmissionAwaitingReview>(
            exchange, _ => ProgressEventsRouting.RoutingKeys.IssueSubmissionAwaitingReview()).UseDurableOutbox();

        // #383 «Позвать автора»: без этого binding'а событие не маршрутизировалось в RabbitMQ
        // (Wolverine ничего не знал о типе) → уведомление автору «Студенту нужна помощь» молча
        // не отправлялось в проде. RequestAuthorHelp.cs публикует его через outbox.
        opts.PublishMessagesToRabbitMqExchange<IssueSubmissionAuthorHelpRequested>(
            exchange, _ => ProgressEventsRouting.RoutingKeys.IssueSubmissionAuthorHelpRequested()).UseDurableOutbox();

        // #693 «Задать вопрос автору» (до сабмишена) → NotificationService, тип IssueAuthorQuestion.
        // #1156: маршрут отсутствовал с релиза #693 — вопрос сохранялся, уведомление автору не уходило.
        opts.PublishMessagesToRabbitMqExchange<IssueAuthorQuestionAsked>(
            exchange, _ => ProgressEventsRouting.RoutingKeys.IssueAuthorQuestionAsked()).UseDurableOutbox();
    }
}