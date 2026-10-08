using AssignmentReviewService.Core.Features.Installations.Services;
using AssignmentReviewService.Core.Vcs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Shared.GitHubApp;
using GitHubAppOptions = AssignmentReviewService.Core.Features.Installations.Services.GitHubAppOptions;

namespace AssignmentReviewService.Infrastructure.GitHub;

public static class DependencyInjectionExtensions
{
    private static readonly Uri GITHUB_API_BASE = new("https://api.github.com/");

    public static IServiceCollection AddGitHubVcsProvider(
        this IServiceCollection services, IConfiguration configuration)
    {
        // ARS-specific options class (ClientId + per-service SECTION_NAME). Bind вручную
        // потому что AddGitHubAppCore ниже забирает только Shared.GitHubAppOptions —
        // оба класса читают одну секцию, без conflict'а.
        services.AddOptions<GitHubAppOptions>()
            .Bind(configuration.GetSection(GitHubAppOptions.SECTION_NAME))
            .ValidateOnStart();

        // 1.7 hardening (#264): fail-fast при невалидном PEM private key.
        // Без этого malformed key проявлялся mystery 500 на первом webhook вызове.
        services.AddSingleton<IValidateOptions<GitHubAppOptions>, GitHubAppOptionsValidator>();

        // Shared/GitHubApp (issue #296) — token-service + private-key holder.
        // Bind'ит Shared.GitHubApp.GitHubAppOptions на ту же section.
        // Заменяет local'ный IGitHubAppTokenService + GitHubAppTokenService + RSA key loading
        // (ARS до этого имел свой код с двумя багами, теперь оба пофикшены: iat claim
        // в JWT + non-disposable RSA holder — см. doc-комментарии в Shared).
        services.AddGitHubAppCore(configuration, GitHubAppOptions.SECTION_NAME);

        // App-level HttpClient: Shared зарегистрировал базовый — добавляем ARS-specific
        // BaseAddress + Polly resilience. Повторный AddHttpClient<TIface, TImpl> chain'ится
        // на ту же named registration.
        services.AddHttpClient<IGitHubAppTokenService, Shared.GitHubApp.GitHubAppTokenService>(client =>
        {
            client.BaseAddress = GITHUB_API_BASE;
            client.Timeout = TimeSpan.FromSeconds(15);
        }).AddGitHubResilience();

        // VCS API client.
        services.AddHttpClient<IVcsProvider, GitHubVcsProvider>(client =>
        {
            client.BaseAddress = GITHUB_API_BASE;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddGitHubResilience();

        return services;
    }

    /// <summary>
    ///     Polly v8 retry на 5xx + transient HttpRequestException. До 3 попыток,
    ///     exponential backoff 2s baseline + jitter. 401 / 403 / 404 не retry'им —
    ///     это семантический ответ (auth / permissions / missing resource).
    /// </summary>
    private static IHttpClientBuilder AddGitHubResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("github-retry", static pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(2),
                UseJitter = true,
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    (args.Outcome.Result is { } response && (int)response.StatusCode >= 500)),
            });
        });
        return builder;
    }
}
