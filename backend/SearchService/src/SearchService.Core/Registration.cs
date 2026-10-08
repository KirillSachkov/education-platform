using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SearchService.Core.Features.EducationDocuments;
using SearchService.Core.Features.EducationDocuments.Queries;
using SearchService.Core.Features.Reindex.IntegrationEvents;
using SearchService.Core.Messaging;
using SearchService.Core.Reindex;
using TagService.Contracts.HttpCommunication;

namespace SearchService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);
        services.AddOptions<SearchReindexOptions>()
            .Bind(configuration.GetSection(nameof(SearchReindexOptions)))
            .Validate(
                static options => options.ExportBatchSize is > 0 and <= 1000,
                "SearchReindexOptions:ExportBatchSize must be between 1 and 1000")
            .Validate(
                static options => options.ImportBatchSize is > 0 and <= 1000,
                "SearchReindexOptions:ImportBatchSize must be between 1 and 1000")
            .Validate(
                static options => options.DelayBetweenBatchesMs is >= 0 and <= 60_000,
                "SearchReindexOptions:DelayBetweenBatchesMs must be between 0 and 60000")
            .Validate(
                static options => options.ReindexGeneration >= 0,
                "SearchReindexOptions:ReindexGeneration cannot be negative")
            .Validate(
                static options => options.Reconciliation.Interval > TimeSpan.Zero,
                "SearchReindexOptions:Reconciliation:Interval must be positive")
            .Validate(
                static options => options.Reconciliation.MinIntervalSinceLast >= TimeSpan.Zero,
                "SearchReindexOptions:Reconciliation:MinIntervalSinceLast cannot be negative")
            .Validate(
                static options => options.Reconciliation.StartupJitter >= TimeSpan.Zero,
                "SearchReindexOptions:Reconciliation:StartupJitter cannot be negative")
            .ValidateOnStart();

        services.AddEducationServiceHttpCommunication(configuration);
        services.AddTagServiceHttpCommunication(configuration);
        services.AddScoped<EducationDocumentService>();
        services.AddScoped<EducationSearchAccessFilterBuilder>();
        services.AddScoped<ISearchIndexingConsumerController, WolverineSearchIndexingConsumerController>();
        services.AddScoped<FullSearchReindexRequestedHandler>();
        services.AddScoped<CoursesSearchReindexRequestedHandler>();
        services.AddScoped<ModulesSearchReindexRequestedHandler>();
        services.AddScoped<ProjectsSearchReindexRequestedHandler>();
        services.AddScoped<MaterialsSearchReindexRequestedHandler>();
        services.AddScoped<IssuesSearchReindexRequestedHandler>();

        return services;
    }
}
