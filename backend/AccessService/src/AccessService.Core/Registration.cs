using AccessService.Core.Diagnostics;
using AccessService.Core.Features.Billing;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Core.Features.Integrations.GitHubApp;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Core.Features.Integrations.GitHubApp.UseCases;
using AccessService.Core.Features.PlanGrants.IntegrationEvents;
using AccessService.Core.Features.PlanGrants.Services;
using AccessService.Core.Features.Plans;
using Core.Abstractions;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shared.GitHubApp;
using GitHubAppOptions = AccessService.Core.Features.Integrations.GitHubApp.GitHubAppOptions;

namespace AccessService.Core;

public static class Registration
{
    public static IServiceCollection AddCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHandlers(typeof(Registration).Assembly);
        services.AddValidatorsFromAssembly(typeof(Registration).Assembly);
        services.AddEndpoints(typeof(Registration).Assembly);

        // Рантайм-флаг приёма прямой оплаты (T-Bank) — дефолт из конфига
        // (Billing:DefaultEnabled), override админом через PATCH /access/billing-config.
        services.AddOptions<BillingOptions>()
            .Bind(configuration.GetSection(BillingOptions.SECTION_NAME));

        // Срок пробного доступа (#595) — задаётся платформой, автор не вводит.
        // Default TrialDurationDays = 30.
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SECTION_NAME));

        services.AddGitHubAppIntegration(configuration);

        // Provider-agnostic webhook handler — вызывается T-Bank-specific adapter'ом
        // после signature verification. Legacy generic endpoint удалён в #124.
        services.AddScoped<PaymentWebhookHandler>();
        services.AddScoped<RenewalFailureRecorder>();

        // Phase 2 #112 — upgrade credit calculator (subset-rule).
        services.AddScoped<IUpgradeCreditCalculator, UpgradeCreditCalculator>();
        services.AddScoped<IUserGrantProjection, UserGrantProjection>();

        // T-Bank webhook adapter (#102, F.1.2.1) — нормализует T-Bank notification
        // в PaymentWebhookRequest и зовёт PaymentWebhookHandler.
        services.AddScoped<TBankWebhookHandler>();

        return services;
    }

    private static IServiceCollection AddGitHubAppIntegration(
        this IServiceCollection services, IConfiguration configuration)
    {
        // AccessService-specific options (ClientId + FrontendInstallReturnUrl) — нужны
        // в handler'ах InstallRedirect / InstallCallback. Биндим параллельно с Shared'ом:
        // оба класса читают одну и ту же config-секцию ("GitHubApp"), без conflict'а.
        services.AddOptions<GitHubAppOptions>()
            .Bind(configuration.GetSection(GitHubAppOptions.SECTION_NAME));

        services.AddSingleton<OnboardingMetrics>();

        // Shared/GitHubApp (issue #296) — token-service + private-key holder + state-store.
        // Bind'ит Shared.GitHubApp.GitHubAppOptions на ту же section.
        services.AddGitHubAppCore(configuration, GitHubAppOptions.SECTION_NAME);

        // Wire Redis-backed state store если IConnectionMultiplexer уже есть в DI
        // (registered в Program.cs для prod / docker / dev), иначе — InMemory.
        // Тесты используют InMemory автоматически (Redis registration skipped).
        // Keyspace "access" предотвращает collision с ARS install-state'ом.
        services.AddSingleton<IInstallStateStore<InstallStateData>>(sp =>
        {
            StackExchange.Redis.IConnectionMultiplexer? redis =
                sp.GetService<StackExchange.Redis.IConnectionMultiplexer>();
            if (redis is not null)
            {
                return new RedisInstallStateStore<InstallStateData>(
                    redis,
                    sp.GetRequiredService<ILogger<RedisInstallStateStore<InstallStateData>>>(),
                    keyspace: "access");
            }

            return new InMemoryInstallStateStore<InstallStateData>(
                sp.GetRequiredService<TimeProvider>());
        });

        // HttpClient для GitHub API (используется через IGitHubAppApiClient).
        services.AddHttpClient<IGitHubAppApiClient, GitHubAppApiClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // InstallCallback и Webhook не handler-based, регистрируются прямо.
        services.AddScoped<InstallCallbackHandler>();
        services.AddScoped<GitHubWebhookHandler>();

        return services;
    }
}
