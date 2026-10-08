using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.AI;
using SharedKernel;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Features.ContentDrafts;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Infrastructure.AI.AiSettings;
using MaterialProcessingService.Infrastructure.AI.Configuration;
using MaterialProcessingService.Infrastructure.AI.Timecodes;

namespace MaterialProcessingService.Infrastructure.AI.ContentDrafts;

internal sealed class AiVideoContentGenerator : IVideoContentGenerator
{
    /// <summary>Множитель MaxOutputTokens на retry при <c>finish_reason=length</c>. Issue #110.</summary>
    private const double LENGTH_RETRY_BUDGET_MULTIPLIER = 1.5;

    /// <summary>Максимум retry на single-pass content (после исходного запроса).</summary>
    private const int MAX_LENGTH_RETRIES = 1;

    private readonly IAiClientFactory _aiClientFactory;
    private readonly IAiModelSettingsResolver _settingsResolver;
    private readonly VideoProcessingOptions _processingOptions;
    private readonly ContentAiRequestFactory _requestFactory;
    private readonly AiPipelineMetrics _metrics;
    private readonly ILogger<AiVideoContentGenerator> _logger;

    public AiVideoContentGenerator(
        IAiClientFactory aiClientFactory,
        IAiModelSettingsResolver settingsResolver,
        IOptions<VideoProcessingOptions> processingOptions,
        ContentAiRequestFactory requestFactory,
        AiPipelineMetrics metrics,
        ILogger<AiVideoContentGenerator> logger)
    {
        _aiClientFactory = aiClientFactory;
        _settingsResolver = settingsResolver;
        _processingOptions = processingOptions.Value;
        _requestFactory = requestFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<GeneratedVideoContentResult, Error>> GenerateAsync(
        Transcript transcript,
        TimeSpan duration,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        EffectiveAiModelSettings settings = await _settingsResolver.GetAsync(cancellationToken);
        EffectiveAiModelSlot slot = settings.ContentGeneration;
        VideoProcessingAiModelOptions effectiveOptions = slot.ToOptions().WithModel(modelOverride);

        IAiClient aiClient;
        try
        {
            aiClient = _aiClientFactory.Get(slot.Provider);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(
                ex,
                "AI provider '{Provider}' is not available for content generation",
                slot.Provider);
            return Error.Failure(
                "content.generation.provider_unavailable",
                $"AI-провайдер '{slot.Provider}' недоступен. Проверьте AI:Providers в config.");
        }

        Result<GeneratedVideoContentResult, Error> singlePassResult = await GenerateSinglePassAsync(
            aiClient,
            transcript,
            duration,
            effectiveOptions,
            cancellationToken);

        if (singlePassResult.IsSuccess)
            return singlePassResult.Value;

        _logger.LogWarning(
            "Single-pass content generation failed. Falling back to windowed mode. Error: {Error}",
            singlePassResult.Error);

        List<TranscriptWindow> windows = TranscriptWindowBuilder.Build(transcript, duration, _processingOptions.ChunkSeconds);
        if (windows.Count == 0)
            return singlePassResult.Error;

        // Bounded parallelism: 2 одновременных AI-вызова. Без ограничения провайдер
        // может бить per-account rate-limit, а serial foreach делает 4 окна на 60-min
        // видео ~ 4×300s = 20 минут только chunked-фазы. С concurrency=2 вдвое быстрее
        // при той же нагрузке на API. Language detection: первое успешно завершённое
        // окно обновляет shared `language`; race на запись допустим — окна симметричны
        // и язык в одном видео не меняется.
        string language = transcript.Language;
        using SemaphoreSlim gate = new(initialCount: 2, maxCount: 2);

        Task<Result<GeneratedContentChunk?, Error>>[] tasks = windows
            .Select(async window =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    Result<AiGenerationResult<ContentChunkResponse>, Error> chunkResult =
                        await aiClient.GenerateAsync<ContentChunkResponse>(
                            _requestFactory.CreateChunkRequest(effectiveOptions, window, language),
                            cancellationToken);

                    if (chunkResult.IsFailure)
                        return Result.Failure<GeneratedContentChunk?, Error>(MapAiError(chunkResult.Error));

                    ContentChunkResponse payload = chunkResult.Value.Value;
                    string chunkMarkdown = NormalizeMarkdown(payload.ContentMarkdown);
                    if (string.IsNullOrWhiteSpace(chunkMarkdown))
                        return Result.Success<GeneratedContentChunk?, Error>(null);

                    if (!string.IsNullOrWhiteSpace(payload.Language))
                        language = payload.Language;

                    return Result.Success<GeneratedContentChunk?, Error>(
                        new GeneratedContentChunk(window.StartSeconds, window.EndSeconds, chunkMarkdown));
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToArray();

        Result<GeneratedContentChunk?, Error>[] results = await Task.WhenAll(tasks);

        // Fail-fast на первой ошибке. Result — struct, FirstOrDefault на нём не даёт
        // null, поэтому итерируемся явно.
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].IsFailure)
                return results[i].Error;
        }

        // Сохраняем порядок окон в финальной последовательности — `windows` уже
        // отсортированы по StartSeconds, и Task.WhenAll возвращает results в том же
        // порядке, в котором tasks были созданы.
        List<GeneratedContentChunk> chunks = [.. results
            .Where(r => r.Value is not null)
            .Select(r => r.Value!)];

        if (chunks.Count == 0)
        {
            return Error.Failure(
                "content.generation.invalid",
                "Нейросеть вернула некорректный формат контента");
        }

        Result<AiGenerationResult<VideoContentResponse>, Error> mergeResult =
            await aiClient.GenerateAsync<VideoContentResponse>(
                _requestFactory.CreateMergeRequest(effectiveOptions, language, chunks),
                cancellationToken);

        if (mergeResult.IsFailure)
        {
            return new GeneratedVideoContentResult(
                language,
                string.Join("\n\n", chunks.Select(chunk => chunk.ContentMarkdown)));
        }

        VideoContentResponse mergedPayload = mergeResult.Value.Value;
        string mergedMarkdown = NormalizeMarkdown(mergedPayload.ContentMarkdown);
        if (string.IsNullOrWhiteSpace(mergedMarkdown))
        {
            mergedMarkdown = string.Join("\n\n", chunks.Select(chunk => chunk.ContentMarkdown));
        }

        return new GeneratedVideoContentResult(
            string.IsNullOrWhiteSpace(mergedPayload.Language) ? language : mergedPayload.Language,
            mergedMarkdown);
    }

    private async Task<Result<GeneratedVideoContentResult, Error>> GenerateSinglePassAsync(
        IAiClient aiClient,
        Transcript transcript,
        TimeSpan duration,
        VideoProcessingAiModelOptions effectiveOptions,
        CancellationToken cancellationToken)
    {
        AiGenerationRequest request = _requestFactory.CreateSinglePassRequest(effectiveOptions, transcript, duration);

        Result<AiBudgetAnalysis, Error> analysisResult = await aiClient.AnalyzeAsync(request, cancellationToken);
        if (analysisResult.IsFailure)
            return MapAiError(analysisResult.Error);

        if (!analysisResult.Value.Fits)
            return AiErrors.ContextExceeded();

        Result<AiGenerationResult<VideoContentResponse>, Error> generationResult =
            await aiClient.GenerateAsync<VideoContentResponse>(request, cancellationToken);

        if (generationResult.IsFailure)
            return MapAiError(generationResult.Error);

        // Issue #110: structured-output (JsonSchema) continuation невозможен — JSON не
        // склеивается. Защита: если упёрлись в MaxOutputTokens — один retry с x1.5
        // budget. Если опять truncate'ed — fail с понятным error code.
        int retryAttempt = 0;
        while (generationResult.Value.FinishReason == AiFinishReason.Length
               && retryAttempt < MAX_LENGTH_RETRIES)
        {
            _metrics.RecordTruncation(job: "CONTENT", level: retryAttempt);
            int? bumpedBudget = effectiveOptions.MaxOutputTokens is int existing
                ? Math.Min(
                    (int)Math.Ceiling(existing * LENGTH_RETRY_BUDGET_MULTIPLIER),
                    AiModelSlot.MAX_OUTPUT_TOKENS)
                : null;
            VideoProcessingAiModelOptions retryOptions = new()
            {
                Model = effectiveOptions.Model,
                Provider = effectiveOptions.Provider,
                Temperature = effectiveOptions.Temperature,
                MaxOutputTokens = bumpedBudget,
                TimeoutSeconds = effectiveOptions.TimeoutSeconds,
            };
            AiGenerationRequest retryRequest =
                _requestFactory.CreateSinglePassRequest(retryOptions, transcript, duration);

            _logger.LogInformation(
                "Content generation truncated (finish_reason=length) at MaxOutputTokens={Original}. " +
                "Retry attempt {Attempt} with bumped budget {Bumped}.",
                effectiveOptions.MaxOutputTokens,
                retryAttempt + 1,
                bumpedBudget);

            generationResult = await aiClient.GenerateAsync<VideoContentResponse>(
                retryRequest,
                cancellationToken);

            if (generationResult.IsFailure)
                return MapAiError(generationResult.Error);

            retryAttempt++;
        }

        if (generationResult.Value.FinishReason == AiFinishReason.Length)
        {
            _metrics.RecordTruncation(job: "CONTENT", level: retryAttempt);
            _metrics.RecordContinuationIterations(retryAttempt);
            return Error.Failure(
                "content.output_too_large",
                "Конспект не помещается в бюджет модели даже после retry. Попробуйте укоротить видео или сменить модель.");
        }

        if (retryAttempt > 0)
            _metrics.RecordContinuationIterations(retryAttempt);

        VideoContentResponse payload = generationResult.Value.Value;
        string contentMarkdown = NormalizeMarkdown(payload.ContentMarkdown);
        if (string.IsNullOrWhiteSpace(contentMarkdown))
        {
            return Error.Failure(
                "content.generation.invalid",
                "Нейросеть вернула некорректный формат контента");
        }

        return new GeneratedVideoContentResult(
            string.IsNullOrWhiteSpace(payload.Language) ? transcript.Language : payload.Language,
            contentMarkdown);
    }

    private static Error MapAiError(Error error)
    {
        if (error.Messages.Count == 0)
            return error;

        string code = error.Messages[0].Code;

        return code switch
        {
            "ai.context_exceeded" => Error.Validation(
                "content.context_exceeded",
                "Транскрипция видео не помещается в контекст модели"),
            "ai.response_invalid" => Error.Failure(
                "content.generation.invalid",
                "Нейросеть вернула некорректный формат контента"),
            _ => error
        };
    }

    private static string NormalizeMarkdown(string contentMarkdown) =>
        string.IsNullOrWhiteSpace(contentMarkdown)
            ? string.Empty
            : contentMarkdown.Trim();
}
