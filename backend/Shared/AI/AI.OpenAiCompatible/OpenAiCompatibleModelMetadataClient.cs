using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal sealed class OpenAiCompatibleModelMetadataClient
{
    private const string MODELS_PATH = "models";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AiOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenAiCompatibleModelMetadataClient> _logger;

    public OpenAiCompatibleModelMetadataClient(
        AiOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleModelMetadataClient> logger)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<Result<IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata>, Error>> GetCatalogAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(NormalizeBaseUrl(_options.BaseUrl), MODELS_PATH));

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            HttpClient client = _httpClientFactory.CreateClient(OpenAiCompatibleHttpClients.CHAT);
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            ModelsResponse? modelsResponse = await JsonSerializer.DeserializeAsync<ModelsResponse>(
                stream,
                _jsonOptions,
                cancellationToken);

            Dictionary<string, OpenAiCompatibleModelMetadata> metadataByModel = BuildMetadataCatalog(modelsResponse?.Data ?? []);

            return Result.Success<IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata>, Error>(metadataByModel);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to fetch AI models catalog for {BaseUrl}",
                _options.BaseUrl);

            return Result.Failure<IReadOnlyDictionary<string, OpenAiCompatibleModelMetadata>, Error>(
                AiErrors.ModelMetadataUnavailable());
        }
    }

    private static Uri NormalizeBaseUrl(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private Dictionary<string, OpenAiCompatibleModelMetadata> BuildMetadataCatalog(IReadOnlyList<ModelResponse> models)
    {
        Dictionary<string, OpenAiCompatibleModelMetadata> metadataByModel = new(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, ModelResponse> group in models
                     .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                     .GroupBy(static item => item.Id.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            if (group.Count() > 1)
            {
                _logger.LogInformation(
                    "AI models catalog returned duplicate entries for model {ModelId}; merging {Count} records",
                    group.Key,
                    group.Count());
            }

            metadataByModel[group.Key] = MergeMetadata(group);
        }

        return metadataByModel;
    }

    private static OpenAiCompatibleModelMetadata MergeMetadata(IEnumerable<ModelResponse> items)
    {
        int? contextLength = null;
        HashSet<string> inputModalities = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> supportedParameters = new(StringComparer.OrdinalIgnoreCase);

        foreach (ModelResponse item in items)
        {
            OpenAiCompatibleModelMetadata metadata = MapMetadata(item);

            if (metadata.ContextLength is int currentContextLength &&
                (contextLength is null || currentContextLength > contextLength.Value))
            {
                contextLength = currentContextLength;
            }

            inputModalities.UnionWith(metadata.InputModalities);
            supportedParameters.UnionWith(metadata.SupportedParameters);
        }

        return new OpenAiCompatibleModelMetadata(contextLength, inputModalities, supportedParameters);
    }

    private static OpenAiCompatibleModelMetadata MapMetadata(ModelResponse item) =>
        new(
            // Берём context_length откуда есть: root (OpenAI/RouterAI стандарт)
            // или top_provider (Polza). Root имеет приоритет — если провайдер
            // явно указал на верхнем уровне, скорее это и canonical значение.
            item.ContextLength ?? item.TopProvider?.ContextLength,
            (item.Architecture?.InputModalities ?? [])
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase),
            // supported_parameters Polza тоже держит на top_provider; объединяем
            // оба уровня (root + top_provider) — multi-provider unionу OK.
            ((item.SupportedParameters ?? []).Concat(item.TopProvider?.SupportedParameters ?? []))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase));

    private sealed class ModelsResponse
    {
        public List<ModelResponse>? Data { get; init; }
    }

    private sealed class ModelResponse
    {
        public string Id { get; init; } = string.Empty;

        // Polza держит context_length под top_provider, не на root. Root-level
        // оставляем для совместимости с другими OpenAI-совместимыми провайдерами
        // которые могут отдавать его на root.
        [JsonPropertyName("context_length")]
        public int? ContextLength { get; init; }

        [JsonPropertyName("top_provider")]
        public TopProviderResponse? TopProvider { get; init; }

        public ModelArchitectureResponse? Architecture { get; init; }

        [JsonPropertyName("supported_parameters")]
        public List<string>? SupportedParameters { get; init; }
    }

    private sealed class TopProviderResponse
    {
        [JsonPropertyName("context_length")]
        public int? ContextLength { get; init; }

        [JsonPropertyName("supported_parameters")]
        public List<string>? SupportedParameters { get; init; }
    }

    private sealed class ModelArchitectureResponse
    {
        [JsonPropertyName("input_modalities")]
        public List<string>? InputModalities { get; init; }
    }
}
