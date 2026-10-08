using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using Shared.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

using PlatformDatabase;
namespace SearchService.Core.Messaging;

public static class WolverineConfiguration
{
    public static void AddWolverine(this WebApplicationBuilder builder)
    {
        builder.Host.ConfigureServices((context, services) =>
        {
            string rabbitConnectionString = context.Configuration.GetConnectionString(ConnectionStringNames.RABBIT_MQ)!;
            string postgresConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.DATABASE)!;

            services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
            {
                opts.ApplicationAssembly = typeof(WolverineConfiguration).Assembly;

                opts.ConfigureDurableMessaging(postgresConnectionString);
                opts.ConfigureRabbitMq(rabbitConnectionString);
                opts.ConfigureSearchReindexQueue();
                opts.ConfigureStandardErrorPolicies();

                opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            });
        });
    }

    private static void ConfigureDurableMessaging(this WolverineOptions opts, string postgresConnectionString)
    {
        opts.PersistMessagesWithPostgresql(postgresConnectionString.WithPlatformDefaults(), "search");
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.UsePlatformDurabilityDefaults();
    }

    private static void ConfigureSearchReindexQueue(this WolverineOptions opts)
    {
        const string queueName = "search.reindex";

        // Reindex handlers can run for tens of minutes on cold cache. They are
        // executed in sequence on this local queue so only one reindex runs at
        // a time; subsequent enqueues wait. Resume-from-scratch on cancellation
        // is acceptable because reindex is idempotent (alias swap is atomic).
        opts.LocalQueue(queueName)
            .UseDurableInbox()
            .Sequential();

        opts.PublishMessage<FullSearchReindexRequested>().ToLocalQueue(queueName);
        opts.PublishMessage<CoursesSearchReindexRequested>().ToLocalQueue(queueName);
        opts.PublishMessage<ModulesSearchReindexRequested>().ToLocalQueue(queueName);
        opts.PublishMessage<ProjectsSearchReindexRequested>().ToLocalQueue(queueName);
        opts.PublishMessage<MaterialsSearchReindexRequested>().ToLocalQueue(queueName);
        opts.PublishMessage<IssuesSearchReindexRequested>().ToLocalQueue(queueName);
    }
}
