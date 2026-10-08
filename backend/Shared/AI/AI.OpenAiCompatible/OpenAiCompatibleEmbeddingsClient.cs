using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Shared.AI;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

/// <summary>
///     OpenAI-compatible embeddings через POST <c>/v1/embeddings</c>. Используется для
///     RAG-индексации (chunk → vector) и retrieval (query → vector → top-K).
///     <para>
///         Endpoint resolved через <c>BaseUrl</c> из <see cref="AiOptions"/>. Зеркалит
///         convention sibling-клиентов (chat / transcription) — BaseUrl уже содержит
///         <c>/v1/</c> суффикс, относительный path — <c>"embeddings"</c>.
///     </para>
///     <para>
///         Retry на 5xx / transient HttpRequestException делается на уровне
///         HttpClient pipeline через Polly (<see cref="DependencyInjectionExtensions.AddProviderResilience"/>).
///         Здесь — single-shot HTTP вызов; resilience прозрачен.
///     </para>
/// </summary>
public sealed class OpenAiCompatibleEmbeddingsClient : IAiEmbeddingsClient
{
    private const int DEFAULT_TIMEOUT_SECONDS = 60;
    private const string ENDPOINT_PATH = "embeddings";
    private const int MAX_INPUTS_PER_REQUEST = 64;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenAiCompatibleEmbeddingsClient> _logger;

    public OpenAiCompatibleEmbeddingsClient(
        AiOptions options,
        IHttpClientFactory httpClientFactory,
        ILogger<OpenAiCompatibleEmbeddingsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<Result<AiEmbeddingsResult, Error>> EmbedAsync(
        AiEmbeddingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return AiErrors.ProviderUnauthorized();

        if (request.Inputs.Count == 0)
            return AiErrors.EmbeddingsInputsEmpty();

        if (request.Inputs.Count > MAX_INPUTS_PER_REQUEST)
            return AiErrors.EmbeddingsTooManyInputs(MAX_INPUTS_PER_REQUEST);

        TimeSpan effectiveTimeout = request.Timeout
            ?? TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS));
        Uri endpoint = new(NormalizeBaseUrl(_options.BaseUrl), ENDPOINT_PATH);

        HttpClient httpClient = _httpClientFactory.CreateClient(OpenAiCompatibleHttpClients.EMBEDDINGS);

        var body = new EmbeddingsRequestBody
        {
            Model = request.Model,
            Input = request.Inputs,
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(effectiveTimeout);

        // Authorization кладём в HttpRequestMessage — фабрика возвращает per-call
        // HttpClient (handler pooled), а ApiKey/Authorization per-provider'ные.
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(body, options: _jsonOptions),
        };
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(requestMessage, timeoutCts.Token);

            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning(
                    "Embeddings provider rejected API key: {StatusCode} for model {Model}",
                    response.StatusCode, request.Model);
                return AiErrors.ProviderUnauthorized();
            }

            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                _logger.LogWarning(
                    "Embeddings provider returned {StatusCode} for model {Model}: {Body}",
                    response.StatusCode,
                    request.Model,
                    errorBody.Length > 500 ? errorBody[..500] : errorBody);
                return AiErrors.ProviderFailed();
            }

            EmbeddingsResponseBody? parsed;
            try
            {
                parsed = await response.Content.ReadFromJsonAsync<EmbeddingsResponseBody>(
                    _jsonOptions, timeoutCts.Token);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Embeddings provider returned malformed JSON for model {Model}", request.Model);
                return AiErrors.EmbeddingsResponseInvalid();
            }

            if (parsed is null || parsed.Data is null || parsed.Data.Count == 0)
                return AiErrors.EmbeddingsResponseInvalid();

            return new AiEmbeddingsResult
            {
                Embeddings = parsed.Data
                    .Select(d => new AiEmbedding { Index = d.Index, Vector = d.Embedding })
                    .ToList(),
                InputTokens = parsed.Usage?.PromptTokens ?? 0,
                ModelUsed = string.IsNullOrWhiteSpace(parsed.Model) ? request.Model : parsed.Model,
            };
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                ex, "Embeddings request timed out after {Seconds}s for model {Model}",
                effectiveTimeout.TotalSeconds, request.Model);
            return AiErrors.ProviderTimeout();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Embeddings request failed for model {Model}", request.Model);
            return AiErrors.ProviderFailed();
        }
    }

    private static Uri NormalizeBaseUrl(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private sealed record EmbeddingsRequestBody
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required IReadOnlyList<string> Input { get; init; }
    }

    private sealed record EmbeddingsResponseBody
    {
        [JsonPropertyName("data")]
        public IReadOnlyList<EmbeddingsDataItem>? Data { get; init; }

        [JsonPropertyName("usage")]
        public EmbeddingsUsage? Usage { get; init; }

        [JsonPropertyName("model")]
        public string? Model { get; init; }
    }

    private sealed record EmbeddingsDataItem
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public IReadOnlyList<float> Embedding { get; init; } = Array.Empty<float>();
    }

    private sealed record EmbeddingsUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; init; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; init; }
    }
}
