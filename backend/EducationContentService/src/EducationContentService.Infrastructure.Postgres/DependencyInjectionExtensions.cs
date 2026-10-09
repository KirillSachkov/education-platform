using Core.Database;
using Dapper;
using EducationContentService.Core;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseItems;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Collections;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Infrastructure.Postgres.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ordering;
using SharedKernel.DomainEvents;
using Wolverine.EntityFrameworkCore;

using PlatformDatabase;
namespace EducationContentService.Infrastructure.Postgres;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddInfrastructurePostgres(this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<ICourseMaterialsRepository, CourseMaterialsRepository>();
        services.AddScoped<ICourseQuizzesRepository, CourseQuizzesRepository>();
        services.AddScoped<ICoursesRepository, CoursesRepository>();
        services.AddScoped<IOrderedItemsRepository<Course>>(sp =>
            sp.GetRequiredService<ICoursesRepository>());
        services.AddScoped<ICourseItemsRepository, CourseItemsRepository>();
        services.AddScoped<IOrderedItemsRepository<CourseItem>>(sp =>
            sp.GetRequiredService<ICourseItemsRepository>());
        services.AddScoped<IModulesRepository, ModulesRepository>();
        services.AddScoped<IModuleItemsRepository, ModuleItemsRepository>();
        services.AddScoped<IOrderedItemsRepository<ModuleItem>>(sp =>
            sp.GetRequiredService<IModuleItemsRepository>());
        services.AddScoped<IMaterialsRepository, MaterialsRepository>();
        services.AddScoped<IProjectsRepository, ProjectsRepository>();
        services.AddScoped<IProjectItemsRepository, ProjectItemsRepository>();
        services.AddScoped<IOrderedItemsRepository<ProjectItem>>(sp =>
            sp.GetRequiredService<IProjectItemsRepository>());
        services.AddScoped<IIssuesRepository, IssuesRepository>();
        services.AddScoped<IQuizzesRepository, QuizzesRepository>();
        services.AddScoped<IReviewConfigRepository, ReviewConfigRepository>();
        services.AddScoped<ICollectionsRepository, CollectionsRepository>();
        services.AddScoped<ICollectionSectionsRepository, CollectionSectionsRepository>();
        services.AddScoped<IOrderedItemsRepository<CollectionSection>>(sp =>
            sp.GetRequiredService<ICollectionSectionsRepository>());
        services.AddScoped<ICollectionItemsRepository, CollectionItemsRepository>();
        services.AddScoped<IOrderedItemsRepository<CollectionItem>>(sp =>
            sp.GetRequiredService<ICollectionItemsRepository>());
        services.AddScoped<CourseMaterialService>();

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        services.AddDbContextPool<EducationDbContext>((sp, options) =>
        {
            string? connectionString = configuration.GetConnectionString(ConnectionStringNames.DATABASE);
            IHostEnvironment hostEnvironment = sp.GetRequiredService<IHostEnvironment>();
            ILoggerFactory loggerFactory = sp.GetRequiredService<ILoggerFactory>();

            options.UsePlatformNpgsql(connectionString);

            if (hostEnvironment.IsDevelopment() || string.Equals(hostEnvironment.EnvironmentName, "Docker", StringComparison.Ordinal))
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.UseLoggerFactory(loggerFactory);
        });

        services.AddScoped<ITransactionManager, TransactionManager>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IDbContextOutbox<EducationDbContext>, DbContextOutbox<EducationDbContext>>();

        services.AddDomainEvents();

        return services;
    }
}