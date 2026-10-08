using Core.Database;
using FileService.Core;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Infrastructure.Postgres.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace FileService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<IMediaAssetRepository, MediaAssetRepository>();
        services.AddScoped<IAssetOwnershipCheckpointRepository, AssetOwnershipCheckpointRepository>();
        services.AddScoped<IFileStorageRefRepository, FileStorageRefRepository>();
        services.AddScoped<IVideoProviderRefRepository, VideoProviderRefRepository>();
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();

        services.AddDbContextPool<FileServiceDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);
            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options.UsePlatformNpgsql(connectionString);

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<IDbContextOutbox<FileServiceDbContext>, DbContextOutbox<FileServiceDbContext>>();

        services.AddDomainEvents();

        return services;
    }
}
