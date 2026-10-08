using Core.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProgressService.Core.Abstractions;
using ProgressService.Core.Configuration;
using ProgressService.Core.Features.LevelTests.AiGrading;
using ProgressService.Core.Services;

namespace ProgressService.Core;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GamificationOptions>()
            .Bind(configuration.GetSection(GamificationOptions.SECTION_NAME))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GamificationOptions>, GamificationOptionsValidator>();

        // ST-5 (#480): AI-грейдинг open_text ответов level-test'а. Provider-слой
        // (Shared/AI: AddOpenAiCompatible + IAiClient + AddAiSkills) регистрируется в Web.
        services.AddOptions<LevelTestAiOptions>()
            .Bind(configuration.GetSection(LevelTestAiOptions.SECTION_NAME));

        services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);
        services.AddHandlers(typeof(DependencyInjectionExtensions).Assembly);

        // ST-7 (#482) observability — singleton funnel-metrics aggregator (level-test).
        services.AddSingleton<Diagnostics.ProgressMetrics>();
        services.AddHybridCache();
        services.AddScoped<IModuleProgressService, ModuleProgressService>();
        services.AddScoped<IEnrollmentAnchorService, EnrollmentAnchorService>();
        services.AddScoped<IStaffIssueCompletionService, StaffIssueCompletionService>();
        services.AddSingleton<IXpLevelPolicy, XpLevelPolicy>();
        services.AddScoped<IXpAwardService, XpAwardService>();

        return services;
    }
}
