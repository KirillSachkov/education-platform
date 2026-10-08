using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace EducationContentService.Core.Features.Plans;

public static class CoursePricingClientExtensions
{
    public static IServiceCollection AddCoursePricingClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AccessServiceOptions>()
            .Bind(configuration.GetSection(AccessServiceOptions.SectionName))
            .ValidateOnStart();

        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<ICoursePricingClient, CoursePricingHttpClient>((sp, client) =>
            {
                AccessServiceOptions options = sp.GetRequiredService<IOptions<AccessServiceOptions>>().Value;

                client.BaseAddress = new Uri(options.Url);
                client.Timeout = options.Timeout;
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
