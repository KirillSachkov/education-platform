using Core.Abstractions;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.AI;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Subtitles;
using MaterialProcessingService.Core.Transcripts;

namespace MaterialProcessingService.Core;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VideoProcessingOptions>()
            .Bind(configuration.GetSection(VideoProcessingOptions.SECTION_NAME))
            .Validate(options => options.ChunkSeconds is >= 60 and <= 3600,
                "VideoProcessing:ChunkSeconds must be between 60 and 3600")
            .Validate(options => options.MaxVideoDurationMinutes is >= 1 and <= 1440,
                "VideoProcessing:MaxVideoDurationMinutes must be between 1 and 1440")
            .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultLanguage)
                                 && options.DefaultLanguage.Length <= 16,
                "VideoProcessing:DefaultLanguage is required and must not exceed 16 characters")
            .Validate(options => options.AudioSpeedup is >= 0.5 and <= 2.0,
                "VideoProcessing:AudioSpeedup must be between 0.5 and 2.0")
            .ValidateOnStart();

        services.AddOptions<TimecodeGenerationOptions>()
            .Bind(configuration.GetSection(TimecodeGenerationOptions.SECTION_NAME))
            .Validate(options => options.MinGapBetweenTimecodesSeconds is >= 0 and <= 3600,
                "Timecodes:MinGapBetweenTimecodesSeconds must be between 0 and 3600")
            .ValidateOnStart();
        services.Configure<Configuration.AiPipelineFeatureFlags>(
            configuration.GetSection(Configuration.AiPipelineFeatureFlags.SECTION_NAME));
        services.AddSingleton<ISubtitleRenderer, SrtSubtitleRenderer>();
        services.AddSingleton<AiPipelineMetrics>();
        services.AddScoped<TranscriptPreparationService>();
        services.AddScoped<TimecodeJobEnqueuer>();

        services.AddHandlers(typeof(DependencyInjectionExtensions).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjectionExtensions).Assembly);

        return services;
    }
}
