using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PlatformAuth.HttpClients;
using Polly;
using Polly.Extensions.Http;

namespace TrainerService.Core.Features.Stats.UserLookup;

public static class UserLookupClientExtensions
{
    /// <summary>
    ///     Wires the AuthService user-lookup client (epic #681): <see cref="AuthServiceOptions"/> binding,
    ///     service-token forwarding, a typed <see cref="UserLookupHttpClient"/> (retry + circuit breaker),
    ///     and the <see cref="CachedUserLookupClient"/> HybridCache decorator registered as
    ///     <see cref="IUserLookupClient"/>. Decorator wired manually (Core has no Scrutor dependency); the
    ///     decorator is transient so the transient typed HttpClient isn't captured by a longer-lived scope.
    /// </summary>
    public static IServiceCollection AddUserLookupClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AuthServiceOptions>()
            .Bind(configuration.GetSection(AuthServiceOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Registers ServiceClientOptions (Authentication:ServiceClient), ServiceTokenProvider and the
        // TokenForwardingHandler so the S2S call carries the service JWT. Idempotent.
        services.AddServiceTokenForwarding(configuration);
        services.AddHybridCache();

        services.AddHttpClient<UserLookupHttpClient>((sp, client) =>
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

        services.AddTransient<IUserLookupClient>(sp => new CachedUserLookupClient(
            sp.GetRequiredService<UserLookupHttpClient>(),
            sp.GetRequiredService<HybridCache>(),
            sp.GetRequiredService<IOptions<AuthServiceOptions>>()));

        return services;
    }
}
