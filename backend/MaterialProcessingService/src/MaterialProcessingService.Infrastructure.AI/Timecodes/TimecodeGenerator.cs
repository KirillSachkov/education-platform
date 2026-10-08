using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.AI;
using SharedKernel;
using MaterialProcessingService.Core;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Features.Timecodes;
using MaterialProcessingService.Core.Transcripts;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Infrastructure.AI.AiSettings;
using MaterialProcessingService.Infrastructure.AI.Configuration;

namespace MaterialProcessingService.Infrastructure.AI.Timecodes;

internal sealed class TimecodeGenerator : ITimecodeGenerator
{
    /// <summary>Множитель MaxOutputTokens на retry при <c>finish_reason=length</c>. Issue #110.</summary>
    private const double LENGTH_RETRY_BUDGET_MULTIPLIER = 1.5;

    private readonly IAiClientFactory _aiClientFactory;
    private readonly IAiModelSettingsResolver _settingsResolver;
    private readonly VideoProcessingOptions _processingOptions;
    private readonly TimecodeGenerationOptions _timecodeOptions;
    private readonly TimecodeAiRequestFactory _requestFactory;
    private readonly AiPipelineMetrics _metrics;
    private readonly ILogger<TimecodeGenerator> _logger;

    public TimecodeGenerator(
        IAiClientFactory aiClientFactory,
        IAiModelSettingsResolver settingsResolver,
        IOptions<VideoProcessingOptions> processingOptions,
        IOptions<TimecodeGenerationOptions> timecodeOptions,
        TimecodeAiRequestFactory requestFactory,
        AiPipelineMetrics metrics,
        ILogger<TimecodeGenerator> logger)
    {
        _aiClientFactory = aiClientFactory;
        _settingsResolver = settingsResolver;
        _processingOptions = processingOptions.Value;
        _timecodeOptions = timecodeOptions.Value;
        _requestFactory = requestFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task<Result<GeneratedTimecodesResult, Error>> GenerateAsync(
        Transcript transcript,
        TimeSpan duration,
        string? modelOverride,
        CancellationToken cancellationToken)
    {
        EffectiveAiModelSettings settings = await _settingsResolver.GetAsync(cancellationToken);
        EffectiveAiModelSlot slot = settings.TimecodeGeneration;
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
                "AI provider '{Provider}' is not available for timecode generation",
                slot.Provider);
            return Error.Failure(
                "timecodes.generation.provider_unavailable",
                $"AI-провайдер '{slot.Provider}' недоступен. Проверьте AI:Providers в config.");
        }

        Result<GeneratedTimecodesResult, Error> singlePassResult = await GenerateSinglePassAsync(
            aiClient,
            transcript,
            duration,
            effectiveOptions,
            cancellationToken);

        if (singlePassResult.IsSuccess)
            return singlePassResult.Value;

        _logger.LogWarning(
            "Single-pass timecode generation failed for model {Model}. Falling back to windowed mode. Error: {Error}",
            effectiveOptions.Model,
            singlePassResult.Error);

        List<TranscriptWindow> windows = TranscriptWindowBuilder.Build(transcript, duration, _processingOptions.ChunkSeconds);
        if (windows.Count == 0)
        {
            return Error.Failure(
                "timecodes.generation.empty",
                "Нейросеть не вернула результат генерации тайм-кодов");
        }

        // Bounded parallelism: 2 одновременных AI-вызова. Per-account rate-limit
        // на provider защищает от бана. На длинных видео с windowed fallback
        // (3-часовое видео = 12 окон) экономия времени ~50%.
        string language = transcript.Language;
        using SemaphoreSlim gate = new(initialCount: 2, maxCount: 2);

        Task<Result<GeneratedWindowTopicsResult, Error>>[] tasks = windows
            .Select(async window =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    return await GenerateWindowTopicsAsync(
                        aiClient,
                        transcript.Language,
                        window,
                        duration,
                        effectiveOptions,
                        cancellationToken);
                }
                finally
                {
                    gate.Release();
                }
            })
            .ToArray();

        Result<GeneratedWindowTopicsResult, Error>[] results = await Task.WhenAll(tasks);

        List<WindowTopicProposal> aggregatedTopics = [];
        for (int i = 0; i < results.Length; i++)
        {
            Result<GeneratedWindowTopicsResult, Error> chunkResult = results[i];

            if (chunkResult.IsFailure)
            {
                if (TimecodePostProcessor.IsSkippableWindowError(chunkResult.Error))
                {
                    _logger.LogWarning(
                        "Skipping invalid timecode generation window {WindowStartSeconds}-{WindowEndSeconds}. Error code: {ErrorCode}",
                        windows[i].StartSeconds,
                        windows[i].EndSeconds,
                        chunkResult.Error.Messages.Count > 0
                            ? chunkResult.Error.Messages[0].Code
                            : null);

                    continue;
                }

                return chunkResult.Error;
            }

            language = string.IsNullOrWhiteSpace(chunkResult.Value.Language)
                ? language
                : chunkResult.Value.Language;

            aggregatedTopics.AddRange(chunkResult.Value.Topics);
        }

        if (aggregatedTopics.Count == 0)
        {
            return Error.Failure(
                "timecodes.generation.invalid",
                "Нейросеть вернула некорректный формат тайм-кодов");
        }

        Result<GeneratedTimecodesResult, Error> mergedResult = await MergeWindowTopicsAsync(
            aiClient,
            language,
            aggregatedTopics,
            duration,
            effectiveOptions,
            cancellationToken);

        if (mergedResult.IsFailure)
            return mergedResult.Error;

        List<GeneratedVideoTimecode> finalizedTimecodes = TimecodePostProcessor.FinalizeFromWindowedFlow(
            mergedResult.Value.Timecodes,
            aggregatedTopics,
            duration,
            _timecodeOptions.MinGapBetweenTimecodesSeconds);

        if (finalizedTimecodes.Count == 0)
        {
            return Error.Failure(
                "timecodes.generation.invalid",
                "Нейросеть вернула некорректный формат тайм-кодов");
        }

        return new GeneratedTimecodesResult(
            mergedResult.Value.Language,
            finalizedTimecodes);
    }

    private async Task<Result<GeneratedTimecodesResult, Error>> GenerateSinglePassAsync(
        IAiClient aiClient,
        Transcript transcript,
        TimeSpan duration,
        VideoProcessingAiModelOptions effectiveOptions,
        CancellationToken cancellationToken)
    {
        AiGenerationRequest request = _requestFactory.CreateSinglePassRequest(effectiveOptions, transcript, duration);

        Result<AiBudgetAnalysis, Error> analysisResult = await aiClient.AnalyzeAsync(request, cancellationToken);
        if (analysisResult.IsFailure)
            return TimecodePostProcessor.MapAiError(analysisResult.Error);

        if (!analysisResult.Value.Fits)
            return AiErrors.ContextExceeded();

        Result<AiGenerationResult<TimecodeGenerationResponse>, Error> generationResult =
            await aiClient.GenerateAsync<TimecodeGenerationResponse>(request, cancellationToken);

        if (generationResult.IsFailure)
            return TimecodePostProcessor.MapAiError(generationResult.Error);

        // Issue #110: при finish_reason=length JSON может быть обрезан (parser упадёт
        // или вернёт неполный список). Один retry с увеличенным MaxOutputTokens — для
        // structured output continuation невозможен, retry единственная опция.
        if (generationResult.Value.FinishReason == AiFinishReason.Length)
        {
            _metrics.RecordTruncation(job: "TIMECODES", level: 0);
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
                "Timecode generation truncated (finish_reason=length) at MaxOutputTokens={Original}. " +
                "Retrying with bumped budget {Bumped}.",
                effectiveOptions.MaxOutputTokens,
                bumpedBudget);

            generationResult = await aiClient.GenerateAsync<TimecodeGenerationResponse>(
                retryRequest,
                cancellationToken);

            if (generationResult.IsFailure)
                return TimecodePostProcessor.MapAiError(generationResult.Error);

            if (generationResult.Value.FinishReason == AiFinishReason.Length)
            {
                _metrics.RecordTruncation(job: "TIMECODES", level: 1);
                return Error.Failure(
                    "timecodes.output_too_large",
                    "Тайм-коды не помещаются в бюджет модели даже после retry. Попробуйте укоротить видео или сменить модель.");
            }
        }

        TimecodeGenerationResponse payload = generationResult.Value.Value;
        List<GeneratedVideoTimecode> mergedTimecodes = TimecodePostProcessor.NormalizeAndMergeFinalTimecodes(
            payload.Timecodes,
            duration,
            _timecodeOptions.MinGapBetweenTimecodesSeconds);

        if (mergedTimecodes.Count == 0)
        {
            return Error.Failure(
                "timecodes.generation.invalid",
                "Нейросеть вернула некорректный формат тайм-кодов");
        }

        return new GeneratedTimecodesResult(
            string.IsNullOrWhiteSpace(payload.Language) ? transcript.Language : payload.Language,
            mergedTimecodes);
    }

    private async Task<Result<GeneratedWindowTopicsResult, Error>> GenerateWindowTopicsAsync(
        IAiClient aiClient,
        string fallbackLanguage,
        TranscriptWindow window,
        TimeSpan duration,
        VideoProcessingAiModelOptions effectiveOptions,
        CancellationToken cancellationToken)
    {
        Result<AiGenerationResult<WindowTopicProposalResponse>, Error> generationResult =
            await aiClient.GenerateAsync<WindowTopicProposalResponse>(
                _requestFactory.CreateWindowTopicsRequest(effectiveOptions, window, duration),
                cancellationToken);

        if (generationResult.IsFailure)
            return TimecodePostProcessor.MapAiError(generationResult.Error);

        WindowTopicProposalResponse payload = generationResult.Value.Value;
        if (payload.Topics.Count == 0)
        {
            return Error.Failure(
                "timecodes.generation.empty",
                "Нейросеть не вернула локальные темы по окну");
        }

        WindowTopicProposalItemResponse[] normalizedItems = TimecodePostProcessor.NormalizeWindowTopics(payload.Topics, window);
        if (normalizedItems.Length == 0)
        {
            return Error.Failure(
                "timecodes.generation.invalid",
                "Нейросеть вернула некорректный формат тайм-кодов");
        }

        WindowTopicProposal[] topics = normalizedItems
            .Where(item => !TimecodePostProcessor.IsGenericTitle(item.Title))
            .Select(item => new WindowTopicProposal(
                item.StartSeconds,
                item.EndSeconds,
                item.Title,
                item.Evidence,
                item.Confidence))
            .ToArray();

        if (topics.Length == 0)
        {
            return Error.Failure(
                "timecodes.generation.empty",
                "Нейросеть не вернула локальные темы по окну");
        }

        return new GeneratedWindowTopicsResult(
            string.IsNullOrWhiteSpace(payload.Language) ? fallbackLanguage : payload.Language,
            topics);
    }

    private async Task<Result<GeneratedTimecodesResult, Error>> MergeWindowTopicsAsync(
        IAiClient aiClient,
        string fallbackLanguage,
        IReadOnlyList<WindowTopicProposal> topics,
        TimeSpan duration,
        VideoProcessingAiModelOptions effectiveOptions,
        CancellationToken cancellationToken)
    {
        Result<AiGenerationResult<TimecodeGenerationResponse>, Error> generationResult =
            await aiClient.GenerateAsync<TimecodeGenerationResponse>(
                _requestFactory.CreateMergeRequest(effectiveOptions, topics, duration),
                cancellationToken);

        if (generationResult.IsFailure)
        {
            if (TimecodePostProcessor.IsSkippableWindowError(generationResult.Error))
            {
                return new GeneratedTimecodesResult(
                    fallbackLanguage,
                    TimecodePostProcessor.ConvertTopicsToTimecodes(topics));
            }

            return TimecodePostProcessor.MapAiError(generationResult.Error);
        }

        TimecodeGenerationResponse payload = generationResult.Value.Value;
        if (payload.Timecodes.Count == 0)
        {
            return new GeneratedTimecodesResult(
                string.IsNullOrWhiteSpace(payload.Language) ? fallbackLanguage : payload.Language,
                TimecodePostProcessor.ConvertTopicsToTimecodes(topics));
        }

        GeneratedVideoTimecode[] mergedTimecodes = TimecodePostProcessor.NormalizeFinalTimecodes(payload.Timecodes, duration);
        if (mergedTimecodes.Length == 0)
        {
            return new GeneratedTimecodesResult(
                string.IsNullOrWhiteSpace(payload.Language) ? fallbackLanguage : payload.Language,
                TimecodePostProcessor.ConvertTopicsToTimecodes(topics));
        }

        return new GeneratedTimecodesResult(
            string.IsNullOrWhiteSpace(payload.Language) ? fallbackLanguage : payload.Language,
            mergedTimecodes);
    }
}
