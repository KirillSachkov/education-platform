using FileService.Core.Imaging;
using Microsoft.Extensions.DependencyInjection;

namespace FileService.Infrastructure.Imaging;

public static class DependencyInjectionImagingExtensions
{
    public static IServiceCollection AddImaging(this IServiceCollection services)
    {
        // Singleton shares the native-render concurrency limit across queue handlers.
        services.AddSingleton<IImageVariantRenderer, SkiaVariantRenderer>();
        return services;
    }
}