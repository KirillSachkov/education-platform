using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SearchService.Core;
using SearchService.Core.Database;
using SearchService.Core.Reindex.State;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace SearchService.Infrastructure.Postgres;

public static class Registration
{
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<SearchDbContext>((sp, options) =>
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

        services.AddScoped<IDbContextOutbox<SearchDbContext>, DbContextOutbox<SearchDbContext>>();
        services.AddScoped<IOutboxService, OutboxService>();

        services.AddScoped<ISearchReindexStateRepository, SearchReindexStateRepository>();

        return services;
    }
}
