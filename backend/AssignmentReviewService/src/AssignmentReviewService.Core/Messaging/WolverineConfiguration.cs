using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformDatabase;
using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Progress;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace AssignmentReviewService.Core.Messaging;

public static class WolverineConfiguration
{
    private const string EDUCATION_REVIEW_CONTEXT_SNAPSHOT_QUEUE =
        "assignment_review.education.review_context_snapshot";
    private const string EDUCATION_REVIEW_SPEC_QUEUE = "assignment_review.education.review_spec";
    private const string EDUCATION_HARD_DELETE_QUEUE = "assignment_review.education.cleanup";
    private const string PROGRESS_AWAITING_REVIEW_QUEUE =
        "assignment_review.progress.submission_awaiting_review";

    public static void AddWolverine(this WebApplicationBuilder builder)
    {
        builder.Host.ConfigureServices((context, services) =>
        {
            string? rabbitConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.RABBIT_MQ);
            string postgresConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.DATABASE)!;

            services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
            {
                opts.ApplicationAssembly = typeof(WolverineConfiguration).Assembly;

                opts.ConfigureDurableMessaging(postgresConnectionString);

                if (!string.IsNullOrEmpty(rabbitConnectionString))
                {
                    opts.ConfigureRabbitMq(rabbitConnectionString);
                }

                opts.ConfigureStandardErrorPolicies();

                opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            });
        });
    }

    private static void ConfigureDurableMessaging(this WolverineOptions opts, string postgresConnectionString)
    {
        // Wolverine envelope tables — schema "assignment_review" (same as domain).
        opts.PersistMessagesWithPostgresql(postgresConnectionString.WithPlatformDefaults(), "assignment_review");
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.UsePlatformDurabilityDefaults();

        // Wolverine'овский DefaultExecutionTimeout по умолчанию = 60s и навязывается
        // ambient CancellationToken'ом хендлера. RunAiReviewRequestedHandler гоняет
        // LLM-ревью (reasoning-модель deepseek-v4-pro, десятки-сотни секунд + chunked
        // multi-batch), которое в 60s не укладывается → ct рубится → in-flight HTTP к
        // AITunnel отменяется → review.llm.unavailable (issue #337). Поднимаем потолок
        // как у MaterialProcessingService (тоже долгие AI-джобы). Реальный per-LLM-call
        // лимит навязывает reviewer-slot TimeoutSeconds (CancelAfter) внутри OpenAiCompatibleClient.
        // После #357 обе ветки (auto + manual /run-iteration/) выполняются ЗДЕСЬ — endpoint
        // /run-iteration/ стал thin shell, который только публикует RunAiReviewRequested.
        opts.DefaultExecutionTimeout = TimeSpan.FromHours(2);
    }

    private static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
    {
        opts.UseRabbitMq(new Uri(connectionString))
            .AutoProvision()
            .EnableWolverineControlQueues()
            .UseQuorumQueues()
            .DeclareExchange(EducationEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(AssignmentReviewEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            })
            .DeclareExchange(ProgressEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        // Phase 7 (#15): consume project.review_context.updated — снимает snapshot
        // guidelines в денорм-таблицу для PromptBuilder'а (StoreProjectGuidelinesHandler).
        // RAG-индексация ref-repo выпилена в #320 — остался только snapshot guidelines.
        opts.ListenToRabbitQueue(EDUCATION_REVIEW_CONTEXT_SNAPSHOT_QUEUE, queue =>
        {
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.ProjectReviewContextUpdated());
        });

        // Phase 7 (#15): consume issue.review_spec.updated — снапшотит ReviewSpec
        // в денорм-таблицу через StoreIssueReviewSpecHandler.
        opts.ListenToRabbitQueue(EDUCATION_REVIEW_SPEC_QUEUE, queue =>
        {
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssueReviewSpecUpdated());
        });

        // Phase 13+ (#15) cascade cleanup на issue.hard_deleted.
        opts.ListenToRabbitQueue(EDUCATION_HARD_DELETE_QUEUE, queue =>
        {
            queue.BindExchange(
                EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.IssueHardDeleted());
        });

        // Phase 8 (#15): consume issue_submission.awaiting_review → создаёт AiReview
        // если payload — GitHub PR URL и есть active VCS installation. Идемпотентно
        // через unique constraint на (provider, submission_id).
        opts.ListenToRabbitQueue(PROGRESS_AWAITING_REVIEW_QUEUE, queue =>
        {
            queue.BindExchange(
                ProgressEventsRouting.EXCHANGE,
                ProgressEventsRouting.RoutingKeys.IssueSubmissionAwaitingReview());
        });

        // Phase 7 (#15): publishing — assignment_review.events / ai_review.iteration.completed.
        opts.PublishMessagesToRabbitMqExchange<Shared.Messaging.IntegrationEvents.AssignmentReview.AiReviewIterationCompleted>(
                AssignmentReviewEventsRouting.EXCHANGE,
                _ => AssignmentReviewEventsRouting.RoutingKeys.IterationCompleted())
            .UseDurableOutbox();

        // Phase 8 (#15): publishing — assignment_review.events / ai_review.queued_for_submission.
        opts.PublishMessagesToRabbitMqExchange<Shared.Messaging.IntegrationEvents.AssignmentReview.AiReviewQueuedForSubmission>(
                AssignmentReviewEventsRouting.EXCHANGE,
                _ => AssignmentReviewEventsRouting.RoutingKeys.QueuedForSubmission())
            .UseDurableOutbox();

        // Issue #307: publishing — assignment_review.events / vcs_installation.created.
        // Consumer — AccessService (auto-complete GITHUB_REVIEW_APP onboarding step).
        opts.PublishMessagesToRabbitMqExchange<Shared.Messaging.IntegrationEvents.AssignmentReview.VcsInstallationCreated>(
                AssignmentReviewEventsRouting.EXCHANGE,
                _ => AssignmentReviewEventsRouting.RoutingKeys.VcsInstallationCreated())
            .UseDurableOutbox();

        // Issue #546: publishing — assignment_review.events / ai_review.oversized_skipped.
        // Consumer — NotificationService (уведомление автору «запустите проверку вручную»).
        opts.PublishMessagesToRabbitMqExchange<Shared.Messaging.IntegrationEvents.AssignmentReview.AiReviewOversizedSkipped>(
                AssignmentReviewEventsRouting.EXCHANGE,
                _ => AssignmentReviewEventsRouting.RoutingKeys.OversizedSkipped())
            .UseDurableOutbox();

        // Issue #713: publishing — assignment_review.events / student_pr_question.asked.
        // Студент прокомментировал свой PR (обратный канал к AI-ревью). Consumers —
        // NotificationService (уведомление автору курса) + ProgressService.
        opts.PublishMessagesToRabbitMqExchange<Shared.Messaging.IntegrationEvents.AssignmentReview.StudentPrQuestionAsked>(
                AssignmentReviewEventsRouting.EXCHANGE,
                _ => AssignmentReviewEventsRouting.RoutingKeys.StudentPrQuestionAsked())
            .UseDurableOutbox();
    }
}
