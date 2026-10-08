using AccessService.Core.Features.Billing.Configuration;
using AccessService.Core.Features.Billing.Diagnostics;
using AccessService.Core.Features.Billing.Reconciliation;
using AccessService.Core.Features.Billing.TBank;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace AccessService.Web.Configuration;

/// <summary>
/// Регистрирует payment-related сервисы. Phase F.1.0 — issue #102.
///
/// <para>F.1.0 — bind options + PaymentMetrics. F.1.1 — register
/// <see cref="ITBankClient"/> implementation (<see cref="TBankClient"/>) на named
/// HttpClient <c>"tbank"</c> с Polly retry/CB через <see cref="AddTBankClient"/>.</para>
///
/// Repos <c>IOrderEventsRepository</c> / <c>IIdempotencyKeyRepository</c> регистрируются
/// в <see cref="AccessService.Infrastructure.Postgres.DependencyInjectionExtensions.AddInfrastructurePostgres"/>
/// рядом с остальными репами AccessService — единое место для wiring infra-слоя.
/// </summary>
public static class PaymentsRegistration
{
    public static IServiceCollection AddPaymentsCore(this IServiceCollection services, IConfiguration configuration)
    {
        // TBankOptions.TerminalKey/Password опциональны (см. XML-doc) — без них
        // billing работает в "disabled" режиме. ValidateOnStart() убран чтобы
        // AccessService поднимался в окружениях без T-Bank credentials.
        services
            .AddOptions<TBankOptions>()
            .Bind(configuration.GetSection(TBankOptions.SECTION_NAME));

        services.AddSingleton<PaymentMetrics>();
        services.AddSingleton<TBankReceiptBuilder>();

        services
            .AddOptions<ReconciliationOptions>()
            .Bind(configuration.GetSection(ReconciliationOptions.SECTION_NAME));

        services.AddSingleton<PendingOrderReconciliationService>();

        return services;
    }

    /// <summary>
    /// Регистрирует named HttpClient <c>"tbank"</c> с Polly retry/CB и таймаутом
    /// из <see cref="TBankOptions.HttpTimeoutSeconds"/>. Phase F.1.1 — issue #102.
    ///
    /// <para>Polly: 3 retries с exponential backoff (200ms × 2^n) только на безопасных
    /// read-операциях CheckOrder/GetState/GetCardList. Init/Charge не ретраятся после
    /// неоднозначного transport failure; их восстанавливает durable reconciliation.
    /// Circuit breaker общий: 5 fail / 30s open.</para>
    /// </summary>
    public static IServiceCollection AddTBankClient(this IServiceCollection services)
    {
        IAsyncPolicy<HttpResponseMessage> safeReadRetry = HttpPolicyExtensions
            .HandleTransientHttpError()
            .Or<Exception>(IsTimeoutRejected)
            .WaitAndRetryAsync(3, attempt => TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)));
        IAsyncPolicy<HttpResponseMessage> noRetry = Policy.NoOpAsync<HttpResponseMessage>();

        services.AddHttpClient<ITBankClient, TBankClient>("tbank", (sp, http) =>
            {
                TBankOptions options = sp.GetRequiredService<IOptions<TBankOptions>>().Value;
                http.BaseAddress = new Uri(options.BaseUrl);
                // Polly owns a fresh timeout token per attempt. HttpClient.Timeout would
                // cancel the outer token once and make safe retries impossible.
                http.Timeout = Timeout.InfiniteTimeSpan;
            })
            // POST is the provider protocol for every operation, but only read methods are
            // safe to replay. Retrying Init/Charge after a lost response can create payment
            // ambiguity or a duplicate charge; those paths recover through CheckOrder/GetState.
            .AddPolicyHandler(request => IsSafeReadOperation(request) ? safeReadRetry : noRetry)
            .AddPolicyHandler(HttpPolicyExtensions
                .HandleTransientHttpError()
                .Or<Exception>(IsTimeoutRejected)
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30)))
            .AddPolicyHandler((sp, _) =>
            {
                int seconds = Math.Max(
                    1,
                    sp.GetRequiredService<IOptions<TBankOptions>>().Value.HttpTimeoutSeconds);
                return Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(seconds));
            });

        return services;
    }

    private static bool IsSafeReadOperation(HttpRequestMessage request)
    {
        string operation = request.RequestUri?.Segments.LastOrDefault() ?? string.Empty;
        return operation is "CheckOrder" or "GetState" or "GetCardList";
    }

    private static bool IsTimeoutRejected(Exception exception) =>
        string.Equals(
            exception.GetType().Name,
            "TimeoutRejectedException",
            StringComparison.Ordinal);
}
