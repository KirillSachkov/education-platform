using Core.Abstractions;
using FileService.Core.Services;
using FileService.Core.Services.AssetRegistry;
using FileService.Core.Services.Files;
using FileService.Core.Services.Videos;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FileService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TargetEntityOptions>(configuration.GetSection(nameof(TargetEntityOptions)));
        services.Configure<FilePublicUrlOptions>(configuration.GetSection(nameof(FilePublicUrlOptions)));
        services.Configure<FileMaintenanceOptions>(configuration.GetSection(nameof(FileMaintenanceOptions)));
        services.Configure<VideoMaintenanceOptions>(configuration.GetSection(nameof(VideoMaintenanceOptions)));
        services.Configure<AssetRetentionOptions>(configuration.GetSection(nameof(AssetRetentionOptions)));

        services.AddHybridCache();

        services.AddHandlers(typeof(Registration).Assembly);

        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);

        services.AddScoped<FileContentUrlBuilder>();
        services.AddSingleton<FileServiceMetrics>();
        services.AddScoped<IAssetSlotReplacement, AssetSlotReplacement>();
        services.AddScoped<ITargetEntityAuthorization, TargetEntityAuthorization>();
        services.AddScoped<AssetBindEventPublisher>();
        services.AddScoped<AssetDeletionLifecycleService>();
        services.AddScoped<FileMaintenanceService>();
        services.AddScoped<VideoReconciliationService>();
        services.AddScoped<AssetRetentionService>();
        services.AddScoped<ImageVariantGenerationService>();

        return services;
    }
}
