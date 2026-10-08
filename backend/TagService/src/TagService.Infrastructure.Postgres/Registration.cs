using Core.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TagService.Core;
using TagService.Core.Database;
using TagService.Core.Features.Tags;
using TagService.Infrastructure.Postgres.Database;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace TagService.Infrastructure.Postgres;

public static class Registration
{
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<TagDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);

            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options.UsePlatformNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "tags"));

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<ITagsRepository, TagsRepository>();

        services.AddScoped<ITransactionManager, TransactionManager>();

        services.AddScoped<IOutboxService, OutboxService>();

        services.AddScoped<IDbContextOutbox<TagDbContext>, DbContextOutbox<TagDbContext>>();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;
    }
}
