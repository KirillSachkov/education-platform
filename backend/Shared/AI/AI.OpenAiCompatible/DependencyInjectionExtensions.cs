using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net.Http.Headers;

namespace Shared.AI.OpenAiCompatible;

public static class DependencyInjectionExtensions
{
    public const string PROVIDER_KIND = "OpenAiCompatible";

    /// <summary>
    ///     Backward-compat alias — старое имя провайдера до rename'а 2026-05-08.
    ///     Конфиги/доки могут использовать любой из двух — оба резолвятся в одну
    ///     и ту же регистрацию.
    /// </summary>
    public const string LEGACY_PROVIDER_KIND = "RouterAI";

    /// <summary>
    ///     Зарегистрировать OpenAiCompatible-адаптер во всех известных <c>Kind</c>'ах
    ///     (canonical + legacy alias'ы). Multi-provider factory вызывает per-provider
    ///     constructor с правильным <see cref="AiOptions"/>.
    /// </summary>
    public static AiProviderBuilder AddOpenAiCompatible(this AiProviderBuilder builder)
    {
        builder.AddProvider(
            PROVIDER_KIND,
            chatConstructor: BuildChatClient,
            transcriptionConstructor: BuildTranscriptionClient,
            embeddingsConstructor: BuildEmbeddingsClient,
            registerSharedServices: RegisterSharedHttpClients);
        builder.AddProvider(
            LEGACY_PROVIDER_KIND,
            chatConstructor: BuildChatClient,
            transcriptionConstructor: BuildTranscriptionClient,
            embeddingsConstructor: BuildEmbeddingsClient);
        return builder;
    }

    public static IServiceCollection AddOpenAiCompatible(
        this IServiceCollection services,
        IConfigurationSection aiSection) =>
        services.AddAi(aiSection, static providers => providers.AddOpenAiCompatible());

    /// <summary>Backward-compat alias for old call-sites.</summary>
    public static AiProviderBuilder AddRouterAi(this AiProviderBuilder builder) =>
        builder.AddOpenAiCompatible();

    /// <summary>Backward-compat alias for old call-sites.</summary>
    public static IServiceCollection AddRouterAi(
        this IServiceCollection services,
        IConfigurationSection aiSection) =>
        services.AddOpenAiCompatible(aiSection);

    /// <summary>
    ///     Per-provider chat client. Создаётся фабрикой один раз на провайдера и
    ///     кэшируется в <see cref="AiClientFactory"/>. Внутри клиента — свой
    ///     ConcurrentDictionary с ChatClient'ами по моделям.
    /// </summary>
    private static IAiClient BuildChatClient(IServiceProvider sp, AiOptions options) =>
        new OpenAiCompatibleClient(
            options,
            BuildRequestResolver(sp, options),
            sp.GetRequiredService<ILogger<OpenAiCompatibleClient>>());

    private static IAiTranscriptionClient BuildTranscriptionClient(IServiceProvider sp, AiOptions options) =>
        new OpenAiCompatibleTranscriptionClient(
            options,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<OpenAiCompatibleTranscriptionClient>>());

    /// <summary>
    ///     Per-provider embeddings client. Используется ARS (#15) для RAG-индексации
    ///     код-чанков. Не все OpenAI-compat провайдеры имеют <c>/embeddings</c> endpoint
    ///     (тогда EmbedAsync вернёт <c>Result.Failure</c> с провайдер-specific ошибкой).
    /// </summary>
    private static IAiEmbeddingsClient BuildEmbeddingsClient(IServiceProvider sp, AiOptions options) =>
        new OpenAiCompatibleEmbeddingsClient(
            options,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<OpenAiCompatibleEmbeddingsClient>>());

    private static OpenAiCompatibleRequestResolver BuildRequestResolver(IServiceProvider sp, AiOptions options)
    {
        // Test seam: если в DI зарегистрирован IAiModelCatalog (например, Substitute через
        // `services.AddSingleton<IAiModelCatalog>(mock)`), используем его. В prod-сценарии
        // никто его явно не регистрирует — строим per-provider OpenAiCompatibleModelCatalog.
        // CAUTION: prod-код НЕ должен регистрировать IAiModelCatalog — иначе тот же catalog
        // будет использован для всех провайдеров (вместо per-provider с правильным BaseUrl).
        IAiModelCatalog catalog = sp.GetService<IAiModelCatalog>() ?? BuildModelCatalog(sp, options);

        return new OpenAiCompatibleRequestResolver(
            options,
            catalog,
            sp.GetRequiredService<IAiTokenEstimator>());
    }

    private static OpenAiCompatibleModelCatalog BuildModelCatalog(IServiceProvider sp, AiOptions options)
    {
        OpenAiCompatibleModelMetadataClient metadataClient = new(
            options,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<OpenAiCompatibleModelMetadataClient>>());

        return new OpenAiCompatibleModelCatalog(options, metadataClient);
    }

    private static void RegisterSharedHttpClients(IServiceCollection services)
    {
        // IAiTokenEstimator — provider-agnostic singleton, регистрируем один раз.
        // TryAddSingleton чтобы тесты могли подменить через свой Substitute.
        services.TryAddSingleton<IAiTokenEstimator, OpenAiCompatibleTokenEstimator>();

        // Три named HttpClient'а: chat (короткий timeout, JSON Accept), transcription
        // (длинный timeout, multipart Content-Type выставляется самим Multipart-content'ом)
        // и embeddings (короткий timeout, JSON). Регистрация общая для всех провайдеров —
        // per-provider HttpClient.Timeout не нужен, потому что per-request timeout уже
        // выставляется через CancellationToken + CancelAfter.
        services.AddHttpClient(OpenAiCompatibleHttpClients.CHAT, static (_, client) =>
            {
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddProviderResilience();

        services.AddHttpClient(OpenAiCompatibleHttpClients.TRANSCRIPTION, static (_, client) =>
            {
                client.Timeout = TimeSpan.FromMinutes(20);
            })
            .AddProviderResilience();

        services.AddHttpClient(OpenAiCompatibleHttpClients.EMBEDDINGS, static (_, client) =>
            {
                client.Timeout = TimeSpan.FromMinutes(2);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));
            })
            .AddProviderResilience();
    }

    /// <summary>
    ///     Reusable resilience pipeline для OpenAI-compatible HTTP-клиентов (LLM chat
    ///     + STT). Оба ходят к одному провайдеру (Polza/AITunnel/etc.), который
    ///     может транзиентно отдавать 5xx ("все провайдеры недоступны для STT") —
    ///     без retry просадка provider'а ломает любую генерацию.
    ///
    ///     Стандартный <c>AddStandardResilienceHandler</c> тут не подходит:
    ///     <list type="bullet">
    ///         <item>standard total-request-timeout = 30s обрезал бы STT (15-min audio
    ///             upload + processing) и LLM (1-3 минуты на длинный response).</item>
    ///         <item>standard attempt-timeout = 10s так же мал.</item>
    ///         <item>circuit breaker / rate limiter в standard не нужны — мы и так
    ///             единственные клиенты этих <c>HttpClient</c>'ов.</item>
    ///     </list>
    ///
    ///     Поэтому регистрируем custom pipeline только с retry-стратегией.
    ///     Retry'им только на transient ошибках — 502/503/504/HttpRequestException.
    ///     Остальные 5xx (500 Internal Server Error, 501 Not Implemented и т.п.)
    ///     обычно non-transient — retry просто прожигает budget. 4xx сразу fail
    ///     (наш баг — auth/validation/prompt overflow).
    ///     Бюджет рассчитан под realistic Polza-окна 30-60s «нет доступных
    ///     STT-провайдеров»: 5 попыток × base 5s → суммарно ~75-150s с jitter.
    ///     Per-request timeout — caller-side через CancellationToken (см.
    ///     <see cref="OpenAiCompatibleTranscriptionClient"/>: linked CTS с
    ///     CancelAfter). HttpClient.Timeout оставляем за HttpClientFactory.
    /// </summary>
    private static IHttpClientBuilder AddProviderResilience(this IHttpClientBuilder builder)
    {
        builder.AddResilienceHandler("openai-compatible-retry", static pipeline =>
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromSeconds(30),
                UseJitter = true,
                ShouldHandle = static args => ValueTask.FromResult(
                    args.Outcome.Exception is HttpRequestException ||
                    (args.Outcome.Result is { } response &&
                     response.StatusCode is System.Net.HttpStatusCode.BadGateway
                                          or System.Net.HttpStatusCode.ServiceUnavailable
                                          or System.Net.HttpStatusCode.GatewayTimeout)),
            });
        });
        return builder;
    }
}
