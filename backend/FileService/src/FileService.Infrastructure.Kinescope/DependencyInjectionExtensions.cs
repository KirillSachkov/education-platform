using FileService.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using Polly.Retry;
using Polly.Timeout;

namespace FileService.Infrastructure.Kinescope;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddKinescope(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<KinescopeOptions>(configuration.GetSection(KinescopeOptions.SECTION_NAME));

        services.AddHttpClient<IVideoProvider, KinescopeApiClient>()
            .AddPolicyHandler((sp, _) => GetRetryPolicy(sp.GetRequiredService<IOptions<KinescopeOptions>>().Value))
            .AddPolicyHandler((sp, _) => GetTimeoutPolicy(sp.GetRequiredService<IOptions<KinescopeOptions>>().Value))
            .AddPolicyHandler((sp, _) => GetCircuitBreakerPolicy(sp.GetRequiredService<IOptions<KinescopeOptions>>().Value));

        return services;
    }

    private static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(KinescopeOptions options) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<Exception>(ex => string.Equals(ex.GetType().Name, "TimeoutRejectedException", StringComparison.Ordinal))
            .WaitAndRetryAsync(options.RetryCount, retryAttempt =>
                TimeSpan.FromSeconds(options.RetryBaseDelaySeconds * Math.Pow(2, retryAttempt - 1)));

    private static AsyncTimeoutPolicy<HttpResponseMessage> GetTimeoutPolicy(KinescopeOptions options) =>
        Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(options.TimeoutSeconds));

    private static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(KinescopeOptions options) =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(
                handledEventsAllowedBeforeBreaking: options.CircuitBreakerFailureThreshold,
                durationOfBreak: TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds));
}