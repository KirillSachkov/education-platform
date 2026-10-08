using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Shared.AI;

/// <summary>
///     Singleton-фабрика per-provider'ных AI-клиентов. Лениво конструирует
///     <see cref="IAiClient"/> / <see cref="IAiTranscriptionClient"/> для каждого
///     провайдера и оборачивает их в <c>MeteredAiClient</c> для cost-наблюдаемости.
///     <para>
///     Каждый созданный клиент кэшируется внутри factory и переиспользуется на
///     все последующие <see cref="Get(string)"/> вызовы — конструкторы вызываются
///     один раз. Это важно: <c>OpenAiCompatibleClient</c> держит внутри cache
///     <c>ChatClient</c>'ов по моделям, которые мы не хотим пересоздавать.
///     </para>
/// </summary>
internal sealed class AiClientFactory : IAiClientFactory, IAiTranscriptionClientFactory, IAiEmbeddingsClientFactory
{
    private readonly IReadOnlyDictionary<string, AiProviderAdapter> _adapters;
    private readonly IReadOnlyDictionary<string, AiOptions> _providers;
    private readonly IServiceProvider _services;
    private readonly IMeterFactory _meterFactory;
    private readonly string _defaultProviderName;

    private readonly ConcurrentDictionary<string, IAiClient> _chatCache =
        new(StringComparer.OrdinalIgnoreCase);
    // null = провайдер не поддерживает STT (кэшируем negative result чтобы каждый Get
    // не пересоздавал клиента впустую). См. <see cref="BuildTranscriptionClient"/>.
    private readonly ConcurrentDictionary<string, IAiTranscriptionClient?> _sttCache =
        new(StringComparer.OrdinalIgnoreCase);
    // То же — null = провайдер не поддерживает embeddings. См. <see cref="BuildEmbeddingsClient"/>.
    private readonly ConcurrentDictionary<string, IAiEmbeddingsClient?> _embeddingsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public AiClientFactory(
        IServiceProvider services,
        IOptions<AiProvidersOptions> options,
        AiProviderBuilder providerBuilder,
        IMeterFactory meterFactory)
    {
        _services = services;
        _adapters = providerBuilder.Adapters;
        _meterFactory = meterFactory;

        AiProvidersOptions opts = options.Value;
        _providers = opts.GetEffectiveProviders();
        _defaultProviderName = string.IsNullOrWhiteSpace(opts.Default)
            ? AiProvidersOptions.DEFAULT_PROVIDER_NAME
            : opts.Default;

        if (_providers.Count == 0)
            throw new InvalidOperationException(
                $"No AI providers configured. Set {AiProvidersOptions.SECTION_NAME}:Providers " +
                $"or legacy fields ({AiProvidersOptions.SECTION_NAME}:Kind/BaseUrl/ApiKey).");

        if (!_providers.ContainsKey(_defaultProviderName))
            throw new InvalidOperationException(
                $"Default AI provider '{_defaultProviderName}' is not in " +
                $"{AiProvidersOptions.SECTION_NAME}:Providers. Configured: " +
                string.Join(", ", _providers.Keys));
    }

    public string DefaultProviderName => _defaultProviderName;

    IAiClient IAiClientFactory.Get(string? providerName)
    {
        string name = ResolveName(providerName);
        return _chatCache.GetOrAdd(name, BuildChatClient);
    }

    IAiTranscriptionClient IAiTranscriptionClientFactory.Get(string? providerName)
    {
        string name = ResolveName(providerName);
        IAiTranscriptionClient? client = _sttCache.GetOrAdd(name, BuildTranscriptionClient);
        if (client is null)
        {
            throw new InvalidOperationException(
                $"AI provider '{name}' does not support transcription (no /audio/transcriptions endpoint).");
        }
        return client;
    }

    IAiEmbeddingsClient IAiEmbeddingsClientFactory.Get(string? providerName)
    {
        string name = ResolveName(providerName);
        IAiEmbeddingsClient? client = _embeddingsCache.GetOrAdd(name, BuildEmbeddingsClient);
        if (client is null)
        {
            throw new InvalidOperationException(
                $"AI provider '{name}' does not support embeddings (no /embeddings endpoint).");
        }
        return client;
    }

    private string ResolveName(string? providerName) =>
        string.IsNullOrWhiteSpace(providerName) ? _defaultProviderName : providerName.Trim();

    private IAiClient BuildChatClient(string providerName)
    {
        (AiProviderAdapter adapter, AiOptions options) = ResolveAdapter(providerName);

        IAiClient inner = adapter.ChatConstructor(_services, options);

        ILogger<MeteredAiClient> logger = _services.GetRequiredService<ILogger<MeteredAiClient>>();
        return new MeteredAiClient(inner, _meterFactory, logger);
    }

    private IAiTranscriptionClient? BuildTranscriptionClient(string providerName)
    {
        (AiProviderAdapter adapter, AiOptions options) = ResolveAdapter(providerName);

        if (adapter.TranscriptionConstructor is null)
            return null;

        IAiTranscriptionClient inner = adapter.TranscriptionConstructor(_services, options);

        ILogger<MeteredAiTranscriptionClient> logger = _services.GetRequiredService<ILogger<MeteredAiTranscriptionClient>>();
        return new MeteredAiTranscriptionClient(inner, _meterFactory, logger);
    }

    private IAiEmbeddingsClient? BuildEmbeddingsClient(string providerName)
    {
        (AiProviderAdapter adapter, AiOptions options) = ResolveAdapter(providerName);

        if (adapter.EmbeddingsConstructor is null)
            return null;

        IAiEmbeddingsClient inner = adapter.EmbeddingsConstructor(_services, options);

        ILogger<MeteredAiEmbeddingsClient> logger = _services.GetRequiredService<ILogger<MeteredAiEmbeddingsClient>>();
        return new MeteredAiEmbeddingsClient(inner, _meterFactory, logger);
    }

    private (AiProviderAdapter Adapter, AiOptions Options) ResolveAdapter(string providerName)
    {
        if (!_providers.TryGetValue(providerName, out AiOptions? providerOptions) || providerOptions is null)
        {
            throw new InvalidOperationException(
                $"AI provider '{providerName}' is not configured. " +
                $"Available: {string.Join(", ", _providers.Keys)}");
        }

        if (string.IsNullOrWhiteSpace(providerOptions.Kind))
        {
            throw new InvalidOperationException(
                $"AI provider '{providerName}' has no Kind set in config.");
        }

        if (!_adapters.TryGetValue(providerOptions.Kind, out AiProviderAdapter? adapter) || adapter is null)
        {
            throw new InvalidOperationException(
                $"AI provider '{providerName}' uses Kind='{providerOptions.Kind}' which is not registered. " +
                $"Registered adapters: {string.Join(", ", _adapters.Keys)}");
        }

        return (adapter, providerOptions);
    }
}
