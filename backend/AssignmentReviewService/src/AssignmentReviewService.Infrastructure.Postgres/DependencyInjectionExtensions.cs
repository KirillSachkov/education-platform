using AssignmentReviewService.Core;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Infrastructure.Postgres.Database;
using AssignmentReviewService.Infrastructure.Postgres.Repositories;
using Core.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformDatabase;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

namespace AssignmentReviewService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(
        this IServiceCollection services, IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);

        services.AddDbContextPool<AssignmentReviewServiceDbContext>(options =>
        {
            options.UsePlatformNpgsql(connectionString);
        });

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<AssignmentReviewServiceDbContext>,
            DbContextOutbox<AssignmentReviewServiceDbContext>>();

        services.AddScoped<IVcsInstallationsRepository, VcsInstallationsRepository>();
        services.AddScoped<IAiReviewsRepository, AiReviewsRepository>();
        services.AddScoped<IIssueReviewSpecsRepository, IssueReviewSpecsRepository>();
        services.AddScoped<IProjectReviewGuidelinesRepository, ProjectReviewGuidelinesRepository>();
        services.AddScoped<IAiModelSettingsRepository, AiModelSettingsRepository>();
        services.AddScoped<IAiReviewIterationFeedbackRepository, AiReviewIterationFeedbackRepository>();
        services.AddScoped<IStudentPrMessagesRepository, StudentPrMessagesRepository>();

        services.AddDomainEvents(typeof(DependencyInjectionExtensions).Assembly);

        return services;
    }
}
