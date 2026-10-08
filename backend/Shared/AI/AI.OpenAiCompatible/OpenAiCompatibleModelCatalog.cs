using System.Collections.Concurrent;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal sealed class OpenAiCompatibleModelCatalog : IAiModelCatalog
{
    private const string PROVIDER_NAME_VALUE = "OpenAiCompatible";
    private const int METADATA_CACHE_TTL_MINUTES = 30;

    // Hardcoded fallback по семейству модели. Используется когда `/v1/models`
    // ни на root, ни в top_provider не вернул нужного поля. Polza отдавала rich
    // metadata через top_provider (включая supported_parameters), AITunnel — нет:
    // их /v1/models возвращает минимальный shape (id, created, object, owned_by).
    // Без fallback'а наши известные OpenAI/Claude/Gemini модели через AITunnel
    // считались не-поддерживающими JSON Schema → timecodes/content generation
    // падали с `ai.output.schema_unsupported`. Значения захардкожены по public
    // OpenAI/Anthropic/Google docs — не зависят от агрегатора.
    private static readonly KnownModelFamily[] _knownModels =
    [
        // OpenAI GPT (с и без provider-prefix'а — Polza vs AITunnel)
        new("openai/gpt-4.1",      1_000_000, SupportsJsonSchema: true,  SupportsAudio: false),
        new("openai/gpt-4o",       128_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("openai/gpt-4-turbo",  128_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("openai/gpt-4",        8_192,     SupportsJsonSchema: false, SupportsAudio: false),
        new("openai/gpt-3.5",      16_385,    SupportsJsonSchema: false, SupportsAudio: false),
        new("openai/o1",           200_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("openai/o3",           200_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("openai/gpt-5",        400_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("gpt-4.1",             1_000_000, SupportsJsonSchema: true,  SupportsAudio: false),
        new("gpt-4o",              128_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        new("gpt-5",               400_000,   SupportsJsonSchema: true,  SupportsAudio: false),
        // Anthropic Claude — все 3+ поддерживают structured outputs через response_format.
        new("anthropic/claude-haiku",  200_000, SupportsJsonSchema: true, SupportsAudio: false),
        new("anthropic/claude-sonnet", 200_000, SupportsJsonSchema: true, SupportsAudio: false),
        new("anthropic/claude-opus",   200_000, SupportsJsonSchema: true, SupportsAudio: false),
        new("claude-haiku",            200_000, SupportsJsonSchema: true, SupportsAudio: false),
        new("claude-sonnet",           200_000, SupportsJsonSchema: true, SupportsAudio: false),
        new("claude-opus",             200_000, SupportsJsonSchema: true, SupportsAudio: false),
        // DeepSeek — поддерживает response_format, но не strict JSON Schema.
        new("deepseek/",  64_000, SupportsJsonSchema: false, SupportsAudio: false),
        new("deepseek-",  64_000, SupportsJsonSchema: false, SupportsAudio: false),
        // Google Gemini — multimodal, поддерживает structured outputs.
        new("google/gemini", 1_000_000, SupportsJsonSchema: true, SupportsAudio: true),
        new("gemini-",       1_000_000, SupportsJsonSchema: true, SupportsAudio: true),
    ];

    private sealed record KnownModelFamily(
        string Prefix,
        int ContextLength,
        bool SupportsJsonSchema,
        bool SupportsAudio);

    private static KnownModelFamily? MatchKnownFamily(string model)
    {
        foreach (KnownModelFamily family in _knownModels)
        {
            if (model.StartsWith(family.Prefix, StringComparison.OrdinalIgnoreCase))
                return family;
        }
        return null;
    }

    private static readonly ConcurrentDictionary<string, CatalogCacheEntry?> _catalogCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _catalogLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly AiOptions _options;
    private readonly OpenAiCompatibleModelMetadataClient _metadataClient;

    public OpenAiCompatibleModelCatalog(
        AiOptions options,
        OpenAiCompatibleModelMetadataClient metadataClient)
    {
        _options = options;
        _metadataClient = metadataClient;
    }

    public async Task<Result<AiModelInfo, Error>> GetModelAsync(
        string model,
        CancellationToken cancellationToken)
    {
        OpenAiCompatibleModelMetadata? metadata = await GetModelMetadataAsync(model, cancellationToken);
        KnownModelFamily? family = MatchKnownFamily(model);

        // Audio input: metadata имеет приоритет (если catalog отдал), иначе family hint.
        bool supportsAudioInput = metadata?.InputModalities.Contains("audio") == true
                                  || metadata?.InputModalities.Contains("audio->text") == true
                                  || family?.SupportsAudio == true;

        // response_format поддерживается всеми современными chat моделями;
        // metadata подтверждает явно, family hint — fallback.
        bool supportsJsonResponseFormat =
            metadata?.SupportedParameters.Contains("response_format") == true
            || metadata?.SupportedParameters.Contains("structured_outputs") == true
            || family is not null;

        // Strict JSON Schema (structured_outputs) поддерживают новые OpenAI/Claude/Gemini.
        // Если metadata дала явное подтверждение — приоритет. Иначе family hint.
        // Это критично: AITunnel /v1/models не возвращает supported_parameters,
        // без family fallback'а timecodes/content генерация падала на schema_unsupported.
        bool supportsJsonSchemaResponseFormat =
            metadata?.SupportedParameters.Contains("structured_outputs") == true
            || family?.SupportsJsonSchema == true;

        int contextLength = metadata?.ContextLength ?? 0;
        if (contextLength <= 0)
            contextLength = family?.ContextLength ?? 0;

        return new AiModelInfo(
            model,
            contextLength,
            supportsAudioInput,
            supportsJsonResponseFormat,
            supportsJsonSchemaResponseFormat,
            SupportsSystemMessage: true,
            IsMetadataAvailable: metadata is not null || family is not null);
    }

    private async Task<OpenAiCompatibleModelMetadata?> GetModelMetadataAsync(
        string model,
        CancellationToken cancellationToken)
    {
        string cacheKey = $"{PROVIDER_NAME_VALUE}|{_options.BaseUrl.TrimEnd('/')}";
        if (_catalogCache.TryGetValue(cacheKey, out CatalogCacheEntry? cachedEntry) &&
            cachedEntry is not null &&
            cachedEntry.ExpiresAtUtc > DateTimeOffset.UtcNow)
        {
            return GetMetadataOrNull(cachedEntry.MetadataByModel, model);
        }

        SemaphoreSlim metadataLock = _catalogLocks.GetOrAdd(
            cacheKey,
            static _ => new SemaphoreSlim(1, 1));

        await metadataLock.WaitAsync(cancellationToken);
        try
        {
            if (_catalogCache.TryGetValue(cacheKey, out cachedEntry) &&
                cachedEntry is not null &&
                cachedEntry.ExpiresAtUtc > DateTimeOffset.UtcNow)
            {
                return GetMetadataOrNull(cachedEntry.MetadataByModel, model);
            }

            Result<IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata>, Error> catalogResult =
                await _metadataClient.GetCatalogAsync(cancellationToken);
            if (catalogResult.IsFailure)
                return null;

            _catalogCache[cacheKey] = new CatalogCacheEntry(
                catalogResult.Value,
                DateTimeOffset.UtcNow.AddMinutes(METADATA_CACHE_TTL_MINUTES));

            return GetMetadataOrNull(catalogResult.Value, model);
        }
        finally
        {
            metadataLock.Release();
        }
    }

    private static OpenAiCompatibleModelMetadata? GetMetadataOrNull(
        IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata> metadataByModel,
        string model) =>
        metadataByModel.TryGetValue(model, out OpenAiCompatibleModelMetadata? metadata)
            ? metadata
            : null;

    private sealed record CatalogCacheEntry(
        IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata> MetadataByModel,
        DateTimeOffset ExpiresAtUtc);
}
