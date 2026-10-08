using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shared.Messaging;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Postgresql;

using PlatformDatabase;
namespace EducationContentService.Core.Messaging;

public static class WolverineConfiguration
{
    public static void AddWolverine(this WebApplicationBuilder builder)
    {
        builder.Host.ConfigureServices((context, services) =>
        {
            string? rabbitConnectionString = context.Configuration.GetConnectionString(ConnectionStringNames.RABBIT_MQ);
            string postgresConnectionString =
                context.Configuration.GetConnectionString(ConnectionStringNames.DATABASE)!;

            services.AddWolverine(ExtensionDiscovery.ManualOnly, opts =>
            {
                opts.ApplicationAssembly = typeof(WolverineConfiguration).Assembly;

                opts.PublishMessage<EducationContentService.Core.Features.FileEvents.VerifyFileAssetBinding>()
                    .ToLocalQueue("verify-file-asset-binding")
                    .UseDurableInbox();

                if (!string.IsNullOrEmpty(postgresConnectionString))
                {
                    opts.ConfigureDurableMessaging(postgresConnectionString);
                    opts.AutoBuildMessageStorageOnStartup = context.HostingEnvironment.IsProduction()
                        ? AutoCreate.CreateOnly
                        : AutoCreate.CreateOrUpdate;
                }

                if (!string.IsNullOrEmpty(rabbitConnectionString))
                {
                    opts.ConfigureRabbitMq(rabbitConnectionString);
                }

                opts.ConfigureStandardErrorPolicies();
            });
        });
    }

    private static void ConfigureDurableMessaging(this WolverineOptions opts, string postgresConnectionString)
    {
        opts.PersistMessagesWithPostgresql(postgresConnectionString.WithPlatformDefaults(), "education");
        opts.UseEntityFrameworkCoreTransactions();
        opts.Policies.UseDurableInboxOnAllListeners();
        opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
        opts.UsePlatformDurabilityDefaults();
    }
}
