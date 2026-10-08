using Core.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformDatabase;
using ServiceName.Persistence.Repositories;
using SharedKernel.DomainEvents;

namespace ServiceName.Persistence;

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
