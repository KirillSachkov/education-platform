using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Messaging;
using Shared.Messaging.IntegrationEvents.Education;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.MaterialProcessing;
using Shared.Messaging.IntegrationEvents.MaterialProcessing.Events;
using MaterialProcessingService.Core.Features.ContentDrafts.Processing;
using MaterialProcessingService.Core.Features.Timecodes.Processing;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace MaterialProcessingService.Core.Messaging;

public static class WolverineConfiguration
{
    private const string TIMECODE_GENERATION_QUEUE = "timecode.generation";
    private const string CONTENT_GENERATION_QUEUE = "content.generation";

    // External RabbitMQ-консумеры для cleanup'а данных pipeline'а:
    //   - на удаление material'а в ECS — снимаем его content-jobs
    //   - на удаление видео-asset'а в FileService — снимаем transcript + jobs
    private const string MATERIAL_PROCESSING_EDUCATION_CLEANUP_QUEUE = "material_processing.education.cleanup";
    private const string MATERIAL_PROCESSING_FILE_CLEANUP_QUEUE = "material_processing.file.cleanup";

    // Авто-обработка видео при готовности (issue #648): слушаем file.ready.material.
    private const string MATERIAL_PROCESSING_FILE_READY_QUEUE = "material_processing.file.ready";

    public static void AddWolverine(this WebApplicationBuilder builder)
    {
        builder.Host.ConfigureServices((context, services) =>
        {
            string postgresConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.DATABASE)
                ?? throw new InvalidOperationException("ConnectionStrings:Database is required");

            // RabbitMq уже опциональный для unit/integration тестов: WebFactory обнуляет
            // строку и DisableAllExternalWolverineTransports() глушит транспорт.
            string? rabbitConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.RABBIT_MQ);

            services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
            {
                opts.ApplicationAssembly = typeof(WolverineConfiguration).Assembly;

                opts.ConfigureDurableMessaging(postgresConnectionString);
                opts.UsePlatformDurabilityDefaults();

                if (context.HostingEnvironment.IsProduction())
                {
                    opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOnly;
                }
                else
                {
                    opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                }

                opts.ConfigureTimecodeGenerationQueue();
                opts.ConfigureContentGenerationQueue();

                if (!string.IsNullOrWhiteSpace(rabbitConnectionString))
                {
                    opts.ConfigureRabbitMq(rabbitConnectionString);
                }

                opts.ConfigureStandardErrorPolicies();
            });
        });
    }

    private static void ConfigureDurableMessaging(this WolverineOptions opts, string postgresConnectionString)
    {
        opts.PersistMessagesWithPostgresql(postgresConnectionString, "material_processing");
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.DefaultExecutionTimeout = TimeSpan.FromHours(2);
    }

    private static void ConfigureTimecodeGenerationQueue(this WolverineOptions opts)
    {
        opts.LocalQueue(TIMECODE_GENERATION_QUEUE)
            .UseDurableInbox()
            .Sequential();

        opts.PublishMessage<GenerateTimecodesJob>().ToLocalQueue(TIMECODE_GENERATION_QUEUE);
    }

    private static void ConfigureContentGenerationQueue(this WolverineOptions opts)
    {
        opts.LocalQueue(CONTENT_GENERATION_QUEUE)
            .UseDurableInbox()
            .Sequential();

        opts.PublishMessage<GenerateVideoContentJob>().ToLocalQueue(CONTENT_GENERATION_QUEUE);
    }

    private static void ConfigureRabbitMq(this WolverineOptions opts, string connectionString)
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
            .DeclareExchange(MaterialProcessingEventsRouting.EXCHANGE, exchange =>
            {
                exchange.ExchangeType = ExchangeType.Topic;
                exchange.IsDurable = true;
            });

        // Исходящее: уведомление о падении АВТО-обработки видео (issue #648).
        opts.PublishMessagesToRabbitMqExchange<VideoAutoProcessingFailed>(
            MaterialProcessingEventsRouting.EXCHANGE,
            _ => MaterialProcessingEventsRouting.RoutingKeys.VIDEO_AUTO_PROCESSING_FAILED).UseDurableOutbox();

        opts.ListenToRabbitQueue(MATERIAL_PROCESSING_EDUCATION_CLEANUP_QUEUE, queue =>
        {
            queue.BindExchange(EducationEventsRouting.EXCHANGE,
                EducationEventsRouting.RoutingKeys.MaterialHardDeleted());
        });

        // Слушаем все file.deleted.* — handler сам отфильтрует только VIDEO usage'и
        // (cover/preview/markdown образы pipeline'у безразличны).
        opts.ListenToRabbitQueue(MATERIAL_PROCESSING_FILE_CLEANUP_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE,
                FileEventsRouting.RoutingKeys.Deleted(FileEventsRouting.EntityTypes.MATERIAL));
            queue.BindExchange(FileEventsRouting.EXCHANGE,
                FileEventsRouting.RoutingKeys.Deleted(FileEventsRouting.EntityTypes.COURSE));
        });

        // Авто-обработка видео при готовности (issue #648): VideoReadyForProcessingHandler
        // фильтрует material_video, проверяет тогл и дедуп, enqueue'ит TIMECODES-job.
        opts.ListenToRabbitQueue(MATERIAL_PROCESSING_FILE_READY_QUEUE, queue =>
        {
            queue.BindExchange(FileEventsRouting.EXCHANGE,
                FileEventsRouting.RoutingKeys.Ready(FileEventsRouting.EntityTypes.MATERIAL));
        });
    }
}
