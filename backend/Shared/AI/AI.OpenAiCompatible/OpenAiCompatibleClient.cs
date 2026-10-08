using System.ClientModel;
using System.Collections.Concurrent;
using System.Text.Json;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using SharedKernel;

namespace Shared.AI.OpenAiCompatible;

internal sealed class OpenAiCompatibleClient : IAiClient
{
    private const string PROVIDER_NAME_VALUE = "OpenAiCompatible";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AiOptions _options;
    private readonly OpenAiCompatibleRequestResolver _requestResolver;
    private readonly ILogger<OpenAiCompatibleClient> _logger;
    private readonly ConcurrentDictionary<string, ChatClient> _chatClients = new(StringComparer.Ordinal);

    public OpenAiCompatibleClient(
        AiOptions options,
        OpenAiCompatibleRequestResolver requestResolver,
        ILogger<OpenAiCompatibleClient> logger)
    {
        _options = options;
        _requestResolver = requestResolver;
        _logger = logger;
    }

    public async Task<Result<AiBudgetAnalysis, Error>> AnalyzeAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        Result<OpenAiCompatibleResolvedRequest, Error> resolvedResult = await _requestResolver.ResolveAsync(request, cancellationToken);
        return resolvedResult.IsFailure
            ? resolvedResult.Error
            : resolvedResult.Value.Analysis;
    }

    public async Task<Result<AiGenerationResult<TResponse>, Error>> GenerateAsync<TResponse>(
        AiGenerationRequest request,
        CancellationToken cancellationToken)
    {
        Result<OpenAiCompatibleResolvedRequest, Error> resolvedResult = await _requestResolver.ResolveAsync(request, cancellationToken);
        if (resolvedResult.IsFailure)
            return resolvedResult.Error;

        OpenAiCompatibleResolvedRequest resolved = resolvedResult.Value;
        if (!resolved.Analysis.Fits)
        {
            _logger.LogWarning(
                "AI request exceeds context window for {Provider}/{Model}. Estimated input tokens: {EstimatedInputTokens}, reserved output tokens: {ReservedOutputTokens}, context window: {ContextWindowTokens}",
                PROVIDER_NAME_VALUE,
                resolved.Model,
                resolved.Analysis.EstimatedInputTokens,
                resolved.Analysis.ReservedOutputTokens,
                resolved.Analysis.ContextWindowTokens);

            return AiErrors.ContextExceeded();
        }

        Result<List<ChatMessage>, Error> messagesResult = OpenAiCompatibleChatMessageFactory.Build(
            request,
            resolved.ModelInfo);
        if (messagesResult.IsFailure)
            return messagesResult.Error;

        ChatClient chatClient = _chatClients.GetOrAdd(resolved.Model, static (model, state) =>
        {
            var sdkOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(state.BaseUrl),
                // Без явного значения System.ClientModel применяет дефолтный NetworkTimeout,
                // который обрывает «молчащее» ожидание первого байта у медленных reasoning-моделей
                // (deepseek-v4-pro) задолго до нашего per-request лимита → review.llm.unavailable
                // на ~57s (issue #337). Поднимаем до provider-таймаута; реальный per-request лимит
                // навязывает linked CancellationToken (CancelAfter resolved.TimeoutSeconds) ниже.
                NetworkTimeout = state.NetworkTimeout,
            };

            return new ChatClient(model, new ApiKeyCredential(state.ApiKey), sdkOptions);
        }, new ProviderState(
            NormalizeBaseUrl(_options.BaseUrl).ToString(),
            _options.ApiKey,
            TimeSpan.FromSeconds(Math.Max(_options.TimeoutSeconds ?? 900, 60))));

        Result<ChatCompletionOptions, Error> optionsResult = OpenAiCompatibleChatOptionsFactory.Build(resolved);
        if (optionsResult.IsFailure)
            return optionsResult.Error;

        using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(
            resolved.TimeoutSeconds,
            cancellationToken);

        CancellationToken requestCancellationToken = timeoutCts?.Token ?? cancellationToken;

        try
        {
            ChatCompletion completion = await chatClient.CompleteChatAsync(
                messagesResult.Value,
                optionsResult.Value,
                requestCancellationToken);

            string rawContent = string.Concat(completion.Content.Select(part => part.Text)).Trim();
            if (string.IsNullOrWhiteSpace(rawContent))
            {
                // Reasoning-модели (deepseek-v4-pro) иногда тратят весь MaxOutputTokens на
                // reasoning и не оставляют видимого контента → FinishReason=Length + пустой
                // ответ. Логируем finish_reason + usage, чтобы отличать «модель помолчала»
                // от «упёрлись в бюджет» (последнее лечится подъёмом MaxOutputTokens).
                _logger.LogWarning(
                    "AI returned empty content via {Provider}/{Model}. FinishReason={FinishReason}, OutputTokens={OutputTokens}.",
                    PROVIDER_NAME_VALUE,
                    resolved.Model,
                    completion.FinishReason,
                    completion.Usage?.OutputTokenCount);
                return AiErrors.OutputEmpty();
            }

            string normalizedContent = NormalizeModelOutput(rawContent);
            Result<string, Error> schemaValidationResult = OpenAiCompatibleJsonSchemaValidator.Validate(
                normalizedContent,
                resolved);
            if (schemaValidationResult.IsFailure)
                return schemaValidationResult.Error;

            Result<TResponse, Error> payloadResult = DeserializePayload<TResponse>(
                normalizedContent,
                resolved.OutputMode);

            if (payloadResult.IsFailure)
                return payloadResult.Error;

            return new AiGenerationResult<TResponse>(
                payloadResult.Value,
                PROVIDER_NAME_VALUE,
                resolved.Model,
                MapUsage(completion.Usage),
                MapFinishReason(completion.FinishReason));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError(ex, "AI generation timed out via {Provider}", PROVIDER_NAME_VALUE);
            return AiErrors.ProviderTimeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI generation failed via {Provider}", PROVIDER_NAME_VALUE);
            return OpenAiCompatibleErrorMapper.Map(ex);
        }
    }

    private static Result<TResponse, Error> DeserializePayload<TResponse>(
        string normalizedContent,
        AiOutputMode outputMode)
    {
        if (outputMode == AiOutputMode.Text && typeof(TResponse) == typeof(string))
            return Result.Success<TResponse, Error>((TResponse)(object)normalizedContent);

        TResponse? payload = JsonSerializer.Deserialize<TResponse>(normalizedContent, _jsonOptions);
        return payload is null
            ? AiErrors.OutputInvalid()
            : Result.Success<TResponse, Error>(payload);
    }

    private static string NormalizeModelOutput(string rawContent) =>
        rawContent.StartsWith("```", StringComparison.Ordinal)
            ? rawContent
                .Replace("```json", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("```", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Trim()
            : rawContent;

    private static AiUsage? MapUsage(ChatTokenUsage? usage) =>
        usage is null
            ? null
            : new AiUsage(usage.InputTokenCount, usage.OutputTokenCount, usage.TotalTokenCount);

    private static AiFinishReason MapFinishReason(ChatFinishReason finishReason) =>
        finishReason switch
        {
            ChatFinishReason.Stop => AiFinishReason.Stop,
            ChatFinishReason.Length => AiFinishReason.Length,
            ChatFinishReason.ContentFilter => AiFinishReason.ContentFilter,
            ChatFinishReason.ToolCalls => AiFinishReason.ToolCalls,
            _ => AiFinishReason.Unknown,
        };

    private static Uri NormalizeBaseUrl(string baseUrl) =>
        new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private static CancellationTokenSource? CreateTimeoutTokenSource(
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (timeoutSeconds <= 0)
            return null;

        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        return timeoutCts;
    }

    private sealed record ProviderState(string BaseUrl, string ApiKey, TimeSpan NetworkTimeout);
}
