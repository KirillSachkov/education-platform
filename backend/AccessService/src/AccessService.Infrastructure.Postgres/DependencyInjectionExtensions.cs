using AccessService.Core.Database;
using AccessService.Infrastructure.Postgres.Database;
using Core.Database;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace AccessService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services, IConfiguration configuration)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        string? connectionString = configuration.GetConnectionString("Database");

        services.AddDbContextPool<AccessServiceDbContext>(options =>
        {
            options.UsePlatformNpgsql(connectionString);
        });

        services.AddScoped<IPlansRepository, PlansRepository>();
        services.AddScoped<IInviteLinksRepository, InviteLinksRepository>();
        services.AddScoped<IPlanGrantsRepository, PlanGrantsRepository>();
        services.AddScoped<IOrdersRepository, OrdersRepository>();
        services.AddScoped<IBillingConfigRepository, BillingConfigRepository>();
        services.AddScoped<IOrderEventsRepository, OrderEventsRepository>();
        services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
        services.AddScoped<IPlanOnboardingFlowsRepository, PlanOnboardingFlowsRepository>();
        services.AddScoped<IUserPlanOnboardingsRepository, UserPlanOnboardingsRepository>();
        services.AddScoped<IPlanPinnedMaterialRepository, PlanPinnedMaterialRepository>();
        services.AddScoped<ITgJoinRemindersRepository, TgJoinRemindersRepository>();
        services.AddScoped<IAuthorGithubInstallationsRepository, AuthorGithubInstallationsRepository>();
        services.AddScoped<IGithubOrgInvitationsRepository, GithubOrgInvitationsRepository>();

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<AccessServiceDbContext>, DbContextOutbox<AccessServiceDbContext>>();

        services.AddDomainEvents(
            typeof(DependencyInjectionExtensions).Assembly,
            typeof(AccessService.Core.Registration).Assembly);

        return services;
    }
}
