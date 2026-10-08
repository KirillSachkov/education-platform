using Shared.Messaging.IntegrationEvents.Access;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Auth;
using Shared.Messaging.IntegrationEvents.Telegram;
using Wolverine;
using Wolverine.RabbitMQ;

namespace AccessService.Core.Messaging;

public static class RabbitMqConfiguration
{
    private const string ACCESS_CONTENT_ACCESS_SYNC_QUEUE = "access.content_access.sync";
    private const string ACCESS_AUTH_GITHUB_EVENTS_QUEUE = "access.auth.github_events";
    private const string ACCESS_PLAN_ONBOARDING_GRANT_EVENTS_QUEUE = "access.plan_onboarding.grant_events";
    private const string ACCESS_PLAN_ONBOARDING_TELEGRAM_EVENTS_QUEUE = "access.plan_onboarding.telegram_events";
    private const string ACCESS_PLAN_ONBOARDING_TELEGRAM_MEMBER_EVENTS_QUEUE = "access.plan_onboarding.telegram_member_events";
    private const string ACCESS_PLAN_ONBOARDING_VCS_EVENTS_QUEUE = "access.plan_onboarding.vcs_events";
    private const string ACCESS_TG_JOIN_REMINDER_GRANT_EVENTS_QUEUE = "access.tg_join_reminder.grant_events";

    public static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(AccessEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AuthEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(TelegramEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AssignmentReviewEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        opts.ConfigureAccessEventsPublishing();
        opts.ConfigureSelfConsumedAccessEvents();
        opts.ConfigureAuthEventsConsumers();
        opts.ConfigurePlanOnboardingConsumers();
        opts.ConfigureTelegramEventsConsumers();
        opts.ConfigureTelegramMemberEventsConsumers();
        opts.ConfigureTgJoinReminderGrantEventsConsumers();
        opts.ConfigureAssignmentReviewEventsConsumers();
    }

    private static void ConfigureSelfConsumedAccessEvents(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_CONTENT_ACCESS_SYNC_QUEUE, queue =>
        {
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantCreated());
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantRevoked());
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantExpired());
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantRenewalRefunded());
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanEntitlementsChanged());
        });

    private static void ConfigurePlanOnboardingConsumers(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_PLAN_ONBOARDING_GRANT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantCreated());
        });

    private static void ConfigureTelegramEventsConsumers(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_PLAN_ONBOARDING_TELEGRAM_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(TelegramEventsRouting.EXCHANGE, TelegramEventsRouting.RoutingKeys.ChatBindingBoundToPlan());
            queue.BindExchange(TelegramEventsRouting.EXCHANGE, TelegramEventsRouting.RoutingKeys.ChatBindingUnboundFromPlan());
        });

    /// <summary>
    ///     Epic #397: consume <see cref="ChatMemberConfirmed"/> from
    ///     <c>telegram.events</c> — auto-complete the TELEGRAM onboarding step when a
    ///     user's join into a plan's community chat is approved. Separate queue from
    ///     <c>access.plan_onboarding.telegram_events</c> (chat-binding lifecycle) so the
    ///     two flows don't share a binding set.
    /// </summary>
    private static void ConfigureTelegramMemberEventsConsumers(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_PLAN_ONBOARDING_TELEGRAM_MEMBER_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(TelegramEventsRouting.EXCHANGE, TelegramEventsRouting.RoutingKeys.ChatMemberConfirmed());
        });

    /// <summary>
    ///     #616 (ST-3): consume <see cref="PlanGrantCreated"/> on a dedicated queue to seed a
    ///     <c>tg_join_reminders</c> tracking row + publish the INITIAL nudge for community-grants
    ///     whose plan has bound Telegram chats. Separate queue from
    ///     <c>access.plan_onboarding.grant_events</c> / <c>access.content_access.sync</c> so the
    ///     tg-join nudge flow doesn't share retry/poison fate with onboarding or Redis-sync.
    /// </summary>
    private static void ConfigureTgJoinReminderGrantEventsConsumers(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_TG_JOIN_REMINDER_GRANT_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AccessEventsRouting.EXCHANGE, AccessEventsRouting.RoutingKeys.PlanGrantCreated());
        });

    /// <summary>
    ///     Issue #307: consume <see cref="VcsInstallationCreated"/> from
    ///     <c>assignment_review.events</c> — auto-complete GITHUB_REVIEW_APP
    ///     onboarding step when user installs the AI-review GitHub App.
    /// </summary>
    private static void ConfigureAssignmentReviewEventsConsumers(this WolverineOptions opts) =>
        opts.ListenToRabbitQueue(ACCESS_PLAN_ONBOARDING_VCS_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(
                AssignmentReviewEventsRouting.EXCHANGE,
                AssignmentReviewEventsRouting.RoutingKeys.VcsInstallationCreated());
        });

    private static void ConfigureAuthEventsConsumers(this WolverineOptions opts)
    {
        opts.ListenToRabbitQueue(ACCESS_AUTH_GITHUB_EVENTS_QUEUE, queue =>
        {
            queue.BindExchange(AuthEventsRouting.EXCHANGE, AuthEventsRouting.RoutingKeys.UserGithubLogin());
        });
    }

    private static void ConfigureAccessEventsPublishing(this WolverineOptions opts)
    {
        string exchange = AccessEventsRouting.EXCHANGE;

        opts.PublishMessagesToRabbitMqExchange<PlanGrantCreated>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantCreated()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRevoked>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRevoked()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantExpired>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantExpired()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<TrialExpiryApproaching>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.TrialExpiryApproaching()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRenewed>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRenewed()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRenewalFailed>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRenewalFailed()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRenewalCancelled>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRenewalCancelled()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRenewalResumed>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRenewalResumed()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanGrantRenewalRefunded>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanGrantRenewalRefunded()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<TgJoinReminderRequested>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.TgJoinReminderRequested()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanCourseBound>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanCourseBound()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanCourseUnbound>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanCourseUnbound()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanEntitlementsChanged>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanEntitlementsChanged()).UseDurableOutbox();

        opts.PublishMessagesToRabbitMqExchange<PlanHardDeleted>(
            exchange,
            _ => AccessEventsRouting.RoutingKeys.PlanHardDeleted()).UseDurableOutbox();
    }
}
