using CommentService.Core;
using CommentService.Core.Database;
using CommentService.Infrastructure.Postgres.Database;
using Core.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace CommentService.Infrastructure.Postgres;

public static class Registration
{
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContextPool<CommentDbContext>(ConfigureDbContext);

        services.AddScoped<ICommentsRepository, CommentsRepository>();

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<CommentDbContext>, DbContextOutbox<CommentDbContext>>();

        services.AddDomainEvents();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;

        void ConfigureDbContext(IServiceProvider sp, DbContextOptionsBuilder options)
        {
            string? connectionString = configuration.GetConnectionString(Constants.DATABASE);

            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options.UsePlatformNpgsql(connectionString);

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        }
    }
}
