using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace TagService.Contracts.HttpCommunication;

public static class TagServiceExtensions
{
    public static IServiceCollection AddTagServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TagServiceOptions>(configuration.GetSection(nameof(TagServiceOptions)));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<ITagServiceClient, TagServiceClient>((sp, config) =>
            {
                TagServiceOptions options = sp.GetRequiredService<IOptions<TagServiceOptions>>().Value;

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
