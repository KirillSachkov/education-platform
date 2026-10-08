using Amazon.S3;
using FileService.Core.FilesStorage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FileService.Infrastructure.S3;

public static class DependencyInjectionS3Extensions
{
    public static IServiceCollection AddS3(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(nameof(FileStorageOptions)))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<FileStorageOptions>, FileStorageOptionsValidator>();

        services.AddScoped<S3Provider>();
        services.AddScoped<IObjectStorageProvider>(sp =>
        {
            FileStorageOptions options = sp.GetRequiredService<IOptions<FileStorageOptions>>().Value;
            S3Provider s3Provider = sp.GetRequiredService<S3Provider>();

            if (!string.IsNullOrWhiteSpace(options.ExternalEndpoint))
            {
                return new ProxiedObjectStorageProvider(s3Provider, options.Endpoint, options.ExternalEndpoint);
            }

            return s3Provider;
        });

        services.AddSingleton<IAmazonS3>(sp =>
        {
            FileStorageOptions fileStorageOptions = sp.GetRequiredService<IOptions<FileStorageOptions>>().Value;

            AmazonS3Config config = new()
            {
                ServiceURL = fileStorageOptions.Endpoint,
                UseHttp = !fileStorageOptions.WithSsl,
                ForcePathStyle = fileStorageOptions.ForcePathStyle
            };

            return new AmazonS3Client(fileStorageOptions.AccessKey, fileStorageOptions.SecretKey, config);
        });

        services.AddHostedService<S3BucketInitializationService>();

        return services;
    }
}
