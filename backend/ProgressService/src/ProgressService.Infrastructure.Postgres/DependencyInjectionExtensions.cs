using AccessService.Contracts.HttpCommunication;
using Core.Database;
using Dapper;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProgressService.Core;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Database;
using ProgressService.Infrastructure.Postgres.Repositories;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace ProgressService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services,
        IConfiguration configuration)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddDbContextPool<ProgressDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);

            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();

            options.UsePlatformNpgsql(connectionString);

            if (hostEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<ProgressDbContext>, DbContextOutbox<ProgressDbContext>>();

        services.AddEducationServiceHttpCommunication(configuration);
        services.AddAuthServiceHttpCommunication(configuration, enableCaching: true);
        services.AddAccessServiceHttpCommunication(configuration);
        services.AddScoped<IContentGrantRepository, ContentGrantRepository>();
        services.AddScoped<ICourseEnrollmentRepository, CourseEnrollmentRepository>();
        services.AddScoped<IProjectProgressRepository, ProjectProgressRepository>();
        services.AddScoped<IIssueProgressRepository, IssueProgressRepository>();
        services.AddScoped<IIssueSubmissionRepository, IssueSubmissionRepository>();
        services.AddScoped<IModuleProgressRepository, ModuleProgressRepository>();
        services.AddScoped<IModuleItemProgressRepository, ModuleItemProgressRepository>();
        services.AddScoped<IMaterialViewRepository, MaterialViewRepository>();
        services.AddScoped<IAnonymousMaterialViewRepository, AnonymousMaterialViewRepository>();
        services.AddScoped<ICoursePositionRepository, CoursePositionRepository>();
        services.AddScoped<IMaterialBookmarkRepository, MaterialBookmarkRepository>();
        services.AddScoped<IIssueAuthorQuestionRepository, IssueAuthorQuestionRepository>();
        services.AddScoped<IQuizAttemptRepository, QuizAttemptRepository>();

        services.AddDomainEvents(typeof(ConnectionStringNames).Assembly);

        return services;
    }
}