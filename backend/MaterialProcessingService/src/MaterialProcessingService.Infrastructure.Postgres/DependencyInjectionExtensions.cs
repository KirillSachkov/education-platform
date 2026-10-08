using Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.DomainEvents;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.Database;
using MaterialProcessingService.Core.Cleanup;
using MaterialProcessingService.Core.Features.AdminInsights;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Infrastructure.Postgres.AdminInsights;
using MaterialProcessingService.Infrastructure.Postgres.Cleanup;
using MaterialProcessingService.Infrastructure.Postgres.Repositories;
using Wolverine.EntityFrameworkCore;

namespace MaterialProcessingService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ITimecodeGenerationJobRepository, TimecodeGenerationJobRepository>();
        services.AddScoped<IContentGenerationJobRepository, ContentGenerationJobRepository>();
        services.AddScoped<IVideoTranscriptRepository, VideoTranscriptRepository>();
        services.AddScoped<IAiModelSettingsRepository, AiModelSettingsRepository>();
        services.AddScoped<IMaterialProcessingCleanup, MaterialProcessingCleanup>();
        services.AddScoped<IAiUsageQueryService, AiUsageQueryService>();
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();

        services.AddDbContextPool<MaterialProcessingServiceDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);
            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options.UseNpgsql(connectionString);

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<IDbContextOutbox<MaterialProcessingServiceDbContext>, DbContextOutbox<MaterialProcessingServiceDbContext>>();
        services.AddDomainEvents();

        return services;
    }
}
