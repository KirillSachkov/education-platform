using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace AuthService.Core.Services;

public static class GitHubOrgServiceExtensions
{
    public static IServiceCollection AddGitHubOrgHttpCommunication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GitHubApiOptions>(configuration.GetSection(GitHubApiOptions.SECTION_NAME));

        services.AddHttpClient<IGitHubOrgService, GitHubOrgService>((sp, client) =>
        {
            GitHubApiOptions options = sp.GetRequiredService<IOptions<GitHubApiOptions>>().Value;

            client.BaseAddress = new Uri(options.Url);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1))))
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)));

        services.AddHttpClient<IGitHubUserService, GitHubUserService>((sp, client) =>
        {
            GitHubApiOptions options = sp.GetRequiredService<IOptions<GitHubApiOptions>>().Value;

            client.BaseAddress = new Uri(options.Url);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        })
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
