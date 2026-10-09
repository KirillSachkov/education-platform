using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Access;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Auth;
using Shared.Messaging.IntegrationEvents.Comments;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.MaterialProcessing;
using Shared.Messaging.IntegrationEvents.Notifications;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Shared.Messaging.IntegrationEvents.Progress;
using Wolverine;
using Wolverine.RabbitMQ;

namespace NotificationService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string NOTIFICATIONS_AUTH_USER_EVENTS_QUEUE = "notifications.auth.user_events";
    private const string NOTIFICATIONS_PROGRESS_SUBMISSION_EVENTS_QUEUE = "notifications.progress.submission_events";
    private const string NOTIFICATIONS_EDUCATION_CONTENT_EVENTS_QUEUE = "notifications.education.content_events";
    private const string NOTIFICATIONS_EDUCATION_COURSE_CREATED_QUEUE = "notifications.education.course_created";
    private const string NOTIFICATIONS_COMMENT_EVENTS_QUEUE = "notifications.comments.thread_events";
    private const string NOTIFICATIONS_ACCESS_GRANT_EVENTS_QUEUE = "notifications.access.grant_events";
    private const string NOTIFICATIONS_ACCESS_TRIAL_REMINDER_EVENTS_QUEUE = "notifications.access.trial_reminder_events";
    private const string NOTIFICATIONS_ACCESS_EXPIRY_EVENTS_QUEUE = "notifications.access.expiry_events";
    private const string NOTIFICATIONS_ACCESS_SUBSCRIPTION_LIFECYCLE_EVENTS_QUEUE =
        "notifications.access.subscription_lifecycle_events";
    private const string NOTIFICATIONS_ACCESS_TG_JOIN_REMINDER_EVENTS_QUEUE = "notifications.access.tg_join_reminder_events";
    private const string NOTIFICATIONS_SELF_SSE_FANOUT_EVENTS_QUEUE = "notifications.self.sse_fanout_events";
    private const string NOTIFICATIONS_SELF_BROADCAST_FANOUT_EVENTS_QUEUE = "notifications.self.broadcast_fanout_events";
    private const string NOTIFICATIONS_CACHE_INVALIDATION_QUEUE = "notifications.cache.invalidation";
    private const string NOTIFICATIONS_TELEGRAM_DELIVERY_EVENTS_QUEUE = "notifications.self.telegram_delivery_events";
    private const string NOTIFICATIONS_ASSIGNMENT_REVIEW_EVENTS_QUEUE = "notifications.assignment_review.review_events";
    private const string NOTIFICATIONS_MATERIAL_PROCESSING_FAILURE_EVENTS_QUEUE = "notifications.material_processing.failure_events";

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
            .DeclareExchange(ProgressEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(EducationEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(CommentEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AccessEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AssignmentReviewEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(MaterialProcessingEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureNotificationEventsPublishing();
        opts.ConfigureSubscriptions();
    }

    private static void ConfigureNotificationEventsPublishing(this WolverineOptions opts)
    {
        string exchange = NotificationsEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<NotificationCreated>(
            exchange,
            _ => NotificationsEventsRouting.RoutingKeys.NotificationCreated()).UseDurableOutbox();

        // NotificationRead routing удалён 2026-05-18 (issue #230 MSG-3) — никто не публиковал
        // его, никто не консьюмил. Если понадобится — re-wire'ить вместе с consumer'ом.

        opts.PublishMessagesToRabbitMqExchange<NotificationBroadcastRequested>(
            exchange,
            _ => NotificationsEventsRouting.RoutingKeys.NotificationBroadcastRequested()).UseDurableOutbox();
    }

    private static void ConfigureSubscriptions(this WolverineOptions opts)
    {
        // auth.events → user.created, user.telegram_linked, user.telegram_unlinked
        opts.ListenToRabbitQueue(NOTIFICATIONS_AUTH_USER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserCreated());
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserTelegramLinked());
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserTelegramUnlinked());
        });

        // progress.events → issue_submission.approved, issue_submission.changes_requested,
        // issue_submission.awaiting_review, issue_submission.author_help_requested (#383),
        // issue.author_question_asked (#693 — студент задал вопрос автору ДО сабмишена)
        opts.ListenToRabbitQueue(NOTIFICATIONS_PROGRESS_SUBMISSION_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueSubmissionApproved());
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueSubmissionChangesRequested());
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueSubmissionAwaitingReview());
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueSubmissionAuthorHelpRequested());
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueAuthorQuestionAsked());
        });

        // education.events → material.published, issue.published.
        // Подписчики курса получают уведомление о новом материале/задании; автор может
        // снять чекбокс в UI publish dialog (NotifySubscribers=false), чтобы не спамить
        // на мелкие правки.
        opts.ListenToRabbitQueue(NOTIFICATIONS_EDUCATION_CONTENT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialPublished());
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssuePublished());
        });

        // education.events → course.created (issue #80): pre-populate subscriptions
        // для юзеров с активным платформенным FULL_ALL/LEARN_ALL grant'ом, чтобы они
        // получали уведомления о новых материалах в этом курсе. Без этого full-access
        // пользователи молчат на новых курсах, потому что обычный CourseEnrolled-flow
        // создаёт subscription только при материализации enrollment'а
        // (которая для нового курса не происходит, доступ работает через plan-tag).
        opts.ListenToRabbitQueue(NOTIFICATIONS_EDUCATION_COURSE_CREATED_QUEUE, queue =>
        {
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.CourseCreated());
        });

        // comment.events → comment.created: CommentReplied / CommentOnOwnContent notifications
        opts.ListenToRabbitQueue(NOTIFICATIONS_COMMENT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(CommentEventsRouting.EXCHANGE, CommentEventsRouting.RoutingKeys.CommentCreated());
        });

        // access.events → plan_grant.created: одно уведомление о доступе по плану
        // (подавляет per-course CourseEnrolled-уведомления когда юзеру открывается
        // несколько курсов одним grant'ом).
        opts.ListenToRabbitQueue(NOTIFICATIONS_ACCESS_GRANT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantCreated());
        });

        // access.events → trial.expiry_approaching (#580): пробный доступ скоро истекает →
        // напоминание самому пользователю доплатить до полного доступа. Отдельная очередь
        // (не grant_events) — другой recipient (сам юзер) и независимый lifecycle.
        opts.ListenToRabbitQueue(NOTIFICATIONS_ACCESS_TRIAL_REMINDER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.TrialExpiryApproaching());
        });

        // access.events → plan_grant.expired (#687): time-limited grant истёк по TTL →
        // уведомление самому пользователю «доступ закончился, продлите». Отдельная очередь
        // (не grant_events / trial_reminder_events) — post-expiry lifecycle, свой recipient-текст.
        opts.ListenToRabbitQueue(NOTIFICATIONS_ACCESS_EXPIRY_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantExpired());
        });

        // access.events → subscription lifecycle (#746): success confirmation, actionable
        // retry/terminal dunning and cancel confirmation. Resume is intentionally silent.
        opts.ListenToRabbitQueue(NOTIFICATIONS_ACCESS_SUBSCRIPTION_LIFECYCLE_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantRenewed());
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantRenewalFailed());
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.PlanGrantRenewalCancelled());
        });

        // access.events → tg_join.reminder_requested (#616): нудж на вступление в Telegram-группу
        // плана. Отдельная очередь — другой recipient (сам юзер), своя стадийная логика каналов
        // (INITIAL без email, REMINDER_* с email) и независимый lifecycle от grant/trial событий.
        opts.ListenToRabbitQueue(NOTIFICATIONS_ACCESS_TG_JOIN_REMINDER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AccessEventsRouting.EXCHANGE,
                AccessEventsRouting.RoutingKeys.TgJoinReminderRequested());
        });

        // assignment_review.events → ai_review.oversized_skipped (#546): авто-ран AI-проверки
        // пропущен (PR выше diff hard-cap'ов) → уведомление автору курса «запустите вручную».
        // + student_pr_question.asked (#713, epic pr-dialogue): студент задал вопрос reply'ем
        // в PR → уведомление автору курса с текстом вопроса + ссылкой на тред.
        opts.ListenToRabbitQueue(NOTIFICATIONS_ASSIGNMENT_REVIEW_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AssignmentReviewEventsRouting.EXCHANGE,
                AssignmentReviewEventsRouting.RoutingKeys.OversizedSkipped());
            queue.BindExchange(
                AssignmentReviewEventsRouting.EXCHANGE,
                AssignmentReviewEventsRouting.RoutingKeys.StudentPrQuestionAsked());
        });

        // material_processing.events → video.auto_processing.failed (#648): авто-обработка
        // видео упала → уведомление владельцу видео с дип-линком в редактор материала.
        opts.ListenToRabbitQueue(NOTIFICATIONS_MATERIAL_PROCESSING_FAILURE_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                MaterialProcessingEventsRouting.EXCHANGE,
                MaterialProcessingEventsRouting.RoutingKeys.VIDEO_AUTO_PROCESSING_FAILED);
        });

        // self: notification.created → SSE fan-out (in-process binding)
        opts.ListenToRabbitQueue(NOTIFICATIONS_SELF_SSE_FANOUT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                NotificationsEventsRouting.EXCHANGE,
                NotificationsEventsRouting.RoutingKeys.NotificationCreated());
        });

        // self: notification.broadcast_requested → разворачивание в N NotificationRequest
        opts.ListenToRabbitQueue(NOTIFICATIONS_SELF_BROADCAST_FANOUT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                NotificationsEventsRouting.EXCHANGE,
                NotificationsEventsRouting.RoutingKeys.NotificationBroadcastRequested());
        });

        // notification.telegram_delivery_recorded → пишем результат TG-доставки в
        // notification_deliveries. Закрывает слепое пятно: NotificationService публиковал
        // notification.created в TG, но не имел никакого фидбека о судьбе сообщения.
        opts.ListenToRabbitQueue(NOTIFICATIONS_TELEGRAM_DELIVERY_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                NotificationsEventsRouting.EXCHANGE,
                NotificationsEventsRouting.RoutingKeys.TelegramDeliveryRecorded());
        });

        // Cache invalidation для CachedEducationContentServiceClient + CachedAuthServiceClient.
        // Подписываемся на 5 routing key из 2-х exchange'ей в одну общую очередь — handler'ы
        // в Core/Messaging/Consumers/CacheInvalidation/ удаляют соответствующие cache-keys.
        opts.ListenToRabbitQueue(NOTIFICATIONS_CACHE_INVALIDATION_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.CourseUpdated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.IssueUpdated());
            queue.BindExchange(EducationEventsRouting.EXCHANGE, EducationEventsRouting.RoutingKeys.MaterialUpdated());
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserUsernameUpdated());
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserDisplayNameUpdated());
        });
    }
}