using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace AuthService.Contracts.HttpCommunication;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddAuthServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableCaching = false)
    {
        services.Configure<AuthServiceOptions>(configuration.GetSection(AuthServiceOptions.SECTION_NAME));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IAuthServiceClient, AuthServiceClient>((sp, client) =>
            {
                AuthServiceOptions options = sp.GetRequiredService<IOptions<AuthServiceOptions>>().Value;

                client.BaseAddress = new Uri(options.Url);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<TokenForwardingHandler>()
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1))))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        if (enableCaching)
        {
            services.AddHybridCache();
            services.Decorate<IAuthServiceClient, CachedAuthServiceClient>();
        }

        return services;
    }
}
