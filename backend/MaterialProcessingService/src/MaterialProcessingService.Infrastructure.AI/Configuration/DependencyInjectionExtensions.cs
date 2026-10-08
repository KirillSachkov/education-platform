using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.AI;
using Shared.AI.OpenAiCompatible;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Features.ContentDrafts;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Infrastructure.AI.AiSettings;
using MaterialProcessingService.Infrastructure.AI.ContentDrafts;
using MaterialProcessingService.Infrastructure.AI.Timecodes;
using MaterialProcessingService.Infrastructure.AI.Transcription;

namespace MaterialProcessingService.Infrastructure.AI.Configuration;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddAiGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        IConfigurationSection aiSection = configuration.GetSection(AiProvidersOptions.SECTION_NAME);
        IConfigurationSection serviceAiSection = configuration.GetSection(VideoProcessingAiOptions.SECTION_NAME);

        VideoProcessingAiOptions options = serviceAiSection.Get<VideoProcessingAiOptions>() ?? new VideoProcessingAiOptions();
        ValidateOptionsResult validationResult = VideoProcessingAiOptionsValidator.Validate(options);
        if (validationResult.Failed)
            throw new InvalidOperationException(validationResult.FailureMessage);

        // Multi-provider regulated entrypoint. Поддерживает оба формата config:
        // - Canonical: "AI": { "Providers": {...}, "Default": "aitunnel" }
        // - Legacy: "AI": { "Kind": "...", "BaseUrl": "...", "ApiKey": "..." }
        //   (транслируется в единственного провайдера с именем "default")
        services.AddAi(aiSection, static providers => providers.AddOpenAiCompatible());

        services
            .AddOptions<VideoProcessingAiOptions>()
            .Bind(serviceAiSection)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<VideoProcessingAiOptions>, VideoProcessingAiOptionsValidator>();

        services.AddMemoryCache();
        services.AddScoped<IAiModelSettingsResolver, AiModelSettingsResolver>();

        services.AddScoped<ISpeechToTextProvider, SpeechToTextProvider>();
        services.AddScoped<ITimecodeGenerator, TimecodeGenerator>();
        services.AddScoped<IVideoContentGenerator, AiVideoContentGenerator>();
        services.AddSingleton<TimecodeAiRequestFactory>();
        services.AddSingleton<ContentAiRequestFactory>();

        return services;
    }
}
