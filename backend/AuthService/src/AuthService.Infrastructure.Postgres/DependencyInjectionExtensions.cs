using Core.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AuthService.Contracts;
using AuthService.Core;
using AuthService.Core.Database;
using AuthService.Infrastructure.Postgres.Repositories;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace AuthService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Dapper: snake_case колонки → PascalCase свойства
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        // Dapper TypeHandler для JSONB-колонки role_profiles
        SqlMapper.AddTypeHandler(new JsonTypeHandler<ProfilesDto>());

        services.AddDbContextPool<AuthDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);
            IHostEnvironment env = sp.GetRequiredService<IHostEnvironment>();

            options.UsePlatformNpgsql(connectionString)
                .UseOpenIddict<Guid>();

            if (env.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<IDbContextOutbox<AuthDbContext>, DbContextOutbox<AuthDbContext>>();
        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IProfileRepository, ProfileRepository>();
        services.AddScoped<IAuthorSpaceRepository, AuthorSpaceRepository>();
        services.AddScoped<IUserGithubOrgRepository, UserGithubOrgRepository>();
        services.AddScoped<IConsentRepository, ConsentRepository>();
        services.AddScoped<IAdminAuditLogWriter, AdminAuditLogWriter>();

        services.AddDomainEvents();

        return services;
    }
}
