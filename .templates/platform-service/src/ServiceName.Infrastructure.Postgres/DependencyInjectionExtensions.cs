using Core.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceName.Core.Database;
using ServiceName.Infrastructure.Postgres.Repositories;
using SharedKernel.DomainEvents;

using PlatformDatabase;
namespace ServiceName.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContextPool<ServiceNameDbContext>(options =>
        {
            options.UsePlatformNpgsql(connectionString);
        });

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IWidgetsRepository, WidgetsRepository>();
        services.AddDomainEvents(typeof(DependencyInjectionExtensions).Assembly);

        return services;
    }
}
