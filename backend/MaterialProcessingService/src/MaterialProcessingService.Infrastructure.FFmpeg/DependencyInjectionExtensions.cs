using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MaterialProcessingService.Core.Media;

namespace MaterialProcessingService.Infrastructure.FFmpeg;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddFFmpeg(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FfmpegOptions>()
            .Bind(configuration.GetSection(FfmpegOptions.SECTION_NAME))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BinaryPath)
                                 && options.BinaryPath.Length <= 1024,
                "Ffmpeg:BinaryPath is required and must not exceed 1024 characters")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ProbeBinaryPath)
                                 && options.ProbeBinaryPath.Length <= 1024,
                "Ffmpeg:ProbeBinaryPath is required and must not exceed 1024 characters")
            .Validate(options => options.ProcessTimeoutSeconds is >= 30 and <= 7200,
                "Ffmpeg:ProcessTimeoutSeconds must be between 30 and 7200")
            .Validate(options => options.TempRootPath is null or { Length: <= 4096 },
                "Ffmpeg:TempRootPath must not exceed 4096 characters")
            .ValidateOnStart();
        services.AddScoped<FfmpegProcessRunner>();
        services.AddScoped<IMediaProbe, FfprobeMediaProbe>();
        services.AddScoped<IAudioExtractor, FfmpegAudioExtractor>();

        return services;
    }
}
