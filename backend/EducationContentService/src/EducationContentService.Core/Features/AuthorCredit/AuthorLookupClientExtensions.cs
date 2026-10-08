using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace EducationContentService.Core.Features.AuthorCredit;

public static class AuthorLookupClientExtensions
{
    public static IServiceCollection AddAuthorLookupClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthServiceOptions>()
            .Bind(configuration.GetSection(AuthServiceOptions.SectionName))
            .ValidateOnStart();

        // Service-token forwarding is registered idempotently (TryAdd-style); safe to wire
        // here even though the course-pricing client also registers it.
        services.AddServiceTokenForwarding(configuration);

        services.AddHttpClient<IAuthorLookupClient, AuthorLookupHttpClient>((sp, client) =>
            {
                AuthServiceOptions options = sp.GetRequiredService<IOptions<AuthServiceOptions>>().Value;

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
