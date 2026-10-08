using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace EducationContentService.Contracts.HttpCommunication;

/// <summary>
///     Регистрация HTTP-клиента EducationContentService в DI-контейнере.
///     Передай <c>enableCaching: true</c> для подключения <see cref="CachedEducationContentServiceClient"/>
///     (lookup-методы кешируются через HybridCache).
/// </summary>
public static class EducationServiceExtensions
{
    public static IServiceCollection AddEducationServiceHttpCommunication(this IServiceCollection services,
        IConfiguration configuration,
        bool enableCaching = false)
    {
        services.Configure<EducationServiceOptions>(configuration.GetSection(nameof(EducationServiceOptions)));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IEducationContentServiceClient, EducationContentServiceClient>((sp, config) =>
            {
                EducationServiceOptions fileOptions = sp.GetRequiredService<IOptions<EducationServiceOptions>>().Value;

                config.BaseAddress = new Uri(fileOptions.Url);

                config.Timeout = TimeSpan.FromSeconds(fileOptions.TimeoutSeconds);
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
            services.Decorate<IEducationContentServiceClient, CachedEducationContentServiceClient>();
        }

        return services;
    }
}
