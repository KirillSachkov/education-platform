using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace AccessService.Contracts.HttpCommunication;

public static class AccessServiceExtensions
{
    public static IServiceCollection AddAccessServiceHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AccessServiceOptions>(configuration.GetSection(AccessServiceOptions.SECTION_NAME));
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IAccessServiceClient, AccessServiceClient>((sp, client) =>
            {
                AccessServiceOptions options = sp.GetRequiredService<IOptions<AccessServiceOptions>>().Value;

                client.BaseAddress = new Uri(options.Url);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            .AddHttpMessageHandler<TokenForwardingHandler>()
            // Phase C: 1 retry с маленькой задержкой — AccessService grant это
            // non-critical enrichment (legacy course-tags уже выданы), не блокируем
            // CourseEnrolled handler thread на долгие ретраи. Worst case ≈ 3s + 250ms + 3s.
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(1, _ => TimeSpan.FromMilliseconds(250)))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        return services;
    }
}
