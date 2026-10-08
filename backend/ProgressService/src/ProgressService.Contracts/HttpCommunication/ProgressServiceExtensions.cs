using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace ProgressService.Contracts.HttpCommunication;

/// <summary>
///     Регистрация HTTP-клиента ProgressService в DI-контейнере consumer'а (ECS).
///     <c>enableCaching: true</c> подключает <see cref="CachedProgressServiceClient"/>
///     для счётчика просмотров (HybridCache, 5min TTL).
/// </summary>
public static class ProgressServiceExtensions
{
    public static IServiceCollection AddProgressServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration,
        bool enableCaching = false)
    {
        services.Configure<ProgressServiceOptions>(configuration.GetSection(nameof(ProgressServiceOptions)));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IProgressServiceClient, ProgressServiceClient>((sp, config) =>
            {
                ProgressServiceOptions options = sp.GetRequiredService<IOptions<ProgressServiceOptions>>().Value;

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

        if (enableCaching)
        {
            services.AddHybridCache();
            services.Decorate<IProgressServiceClient, CachedProgressServiceClient>();
        }

        return services;
    }
}
