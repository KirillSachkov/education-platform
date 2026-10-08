using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace MaterialProcessingService.Contracts.HttpCommunication;

public static class MaterialProcessingServiceExtensions
{
    public static IServiceCollection AddMaterialProcessingServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MaterialProcessingServiceOptions>(
            configuration.GetSection(nameof(MaterialProcessingServiceOptions)));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IMaterialProcessingServiceClient, MaterialProcessingServiceClient>((sp, config) =>
            {
                MaterialProcessingServiceOptions options =
                    sp.GetRequiredService<IOptions<MaterialProcessingServiceOptions>>().Value;

                config.BaseAddress = new Uri(options.Url);
                config.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<TokenForwardingHandler>()
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
