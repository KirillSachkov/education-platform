using System.Net.Http.Headers;
using Core.Abstractions;
using EducationContentService.Core.Features.Collections.Queries;
using EducationContentService.Core.Features.CourseItems;
using EducationContentService.Core.Features.FileEvents;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Core.Features.MaterialProcessing;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Core.Features.Plans;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Core.Features.Roadmaps;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using FileService.Contracts.HttpCommunication;
using MaterialProcessingService.Contracts.HttpCommunication;
using ProgressService.Contracts.HttpCommunication;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ordering;
using Polly;
using Polly.Extensions.Http;

namespace EducationContentService.Core;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<CourseItemService>();
        services.AddScoped<ModuleItemService>();
        services.AddScoped<ProjectItemService>();
        services.AddScoped<RoadmapCleanupService>();
        services.AddScoped<CollectionAccessEnricher>();

        services.AddScoped<OrderingService<Course>>();
        services.AddScoped<OrderingService<CourseItem>>();
        services.AddScoped<OrderingService<ModuleItem>>();
        services.AddScoped<OrderingService<ProjectItem>>();

        services.AddHandlers(typeof(DependencyInjectionExtensions).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);

        // ST-7 (#482) observability — singleton funnel-metrics aggregator (level-test).
        services.AddSingleton<Diagnostics.EducationContentMetrics>();

        services.AddFileServiceHttpCommunication(configuration);
        services.AddMaterialProcessingServiceHttpCommunication(configuration);
        services.AddProgressServiceHttpCommunication(configuration, enableCaching: true);
        services.AddCoursePricingClient(configuration);
        services.AddAuthorLookupClient(configuration);
        services.AddHybridCache();
        services.Decorate<IFileServiceClient, CachedFileServiceClient>();
        services.Decorate<IMaterialProcessingServiceClient, CachedMaterialProcessingClient>();
        services.Decorate<ICoursePricingClient, CachedCoursePricingClient>();
        services.Decorate<IAuthorLookupClient, CachedAuthorLookupClient>();

        services.AddMemoryCache();
        services.AddHttpClient("GitHubApi", client =>
        {
            client.BaseAddress = new Uri("https://api.github.com/");
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("EducationPlatform", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            client.Timeout = TimeSpan.FromSeconds(10);
        })
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1))))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        return services;
    }
}
