using Amazon.S3;
using FileService.Core.Services;
using FileService.Infrastructure.Postgres;
using FileService.Infrastructure.S3;
using FileService.Web.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.Minio;

namespace FileService.IntegrationTests.Infrastructure;

internal static class TestServiceConfiguration
{
    public static void ConfigureDatabase(
        IServiceCollection services,
        string connectionString)
    {
        services.RemoveAll<FileServiceDbContext>();

        services.AddDbContextPool<FileServiceDbContext>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
        });
    }

    public static void ConfigureS3(
        IServiceCollection services,
        MinioContainer minioContainer)
    {
        services.RemoveAll<IAmazonS3>();

        services.AddSingleton<IAmazonS3>(_ =>
        {
            ushort minioPort = minioContainer.GetMappedPublicPort(9000);

            var config = new AmazonS3Config
            {
                ServiceURL = $"http://{minioContainer.Hostname}:{minioPort}",
                UseHttp = true,
                ForcePathStyle = true,
            };

            return new AmazonS3Client("minioadmin", "minioadmin", config);
        });
    }

    public static void ConfigureKinescope(IServiceCollection services)
    {
        services.RemoveAll<IVideoProvider>();
        RemoveHostedService<FileServiceBackgroundJobs>(services);
        RemoveHostedService<S3BucketInitializationService>(services);
        services.AddSingleton(KinescopeMockFactory.Create());
    }

    public static HttpClient CreateHealthyHttpClient()
    {
        return new HttpClient(new HealthyResponseMessageHandler(), disposeHandler: true);
    }

    public static async Task EnsureBucketsCreatedAsync(IAmazonS3 s3Client)
    {
        try
        {
            await s3Client.PutBucketAsync("media");
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "BucketAlreadyOwnedByYou")
        {
            // Bucket already exists, ignore
        }
    }

    private static void RemoveHostedService<THostedService>(IServiceCollection services)
        where THostedService : class, IHostedService
    {
        ServiceDescriptor[] descriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(THostedService))
            .ToArray();

        foreach (ServiceDescriptor descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    private sealed class HealthyResponseMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[]}")
            });
        }
    }
}
