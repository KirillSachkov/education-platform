using System.Diagnostics;
using System.Diagnostics.Metrics;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Shared.AI;

/// <summary>
///     Decorator над любым <see cref="IAiClient"/>, который пишет cost/usage метрики
///     в Meter <c>EducationPlatform.AI</c>:
///     <list type="bullet">
///         <item><c>ai_request_duration_seconds</c> (histogram, tags: model, status)</item>
///         <item><c>ai_input_tokens_total</c> (counter, tags: model)</item>
///         <item><c>ai_output_tokens_total</c> (counter, tags: model)</item>
///         <item><c>ai_requests_total</c> (counter, tags: model, status, finish_reason)</item>
///     </list>
///     Tags позволяют сделать дашборд «сколько ₽/токенов сжёг каждый сервис за сутки».
///     Регистрация: <c>services.Decorate&lt;IAiClient, MeteredAiClient&gt;()</c> или вручную
///     если Decorate-расширения нет в DI.
///
///     Логи: на каждый успешный/failed вызов пишет structured-log с usage — Loki можно
///     использовать как grep-источник для ad-hoc cost-аналитики если меток в Meter не хватает.
/// </summary>
public sealed class MeteredAiClient : IAiClient
{
    private const string METER_NAME = "EducationPlatform.AI";

    private readonly IAiClient _inner;
    private readonly ILogger<MeteredAiClient> _logger;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _inputTokens;
    private readonly Counter<long> _outputTokens;
    private readonly Counter<long> _requests;

    public MeteredAiClient(IAiClient inner, IMeterFactory meterFactory, ILogger<MeteredAiClient> logger)
    {
        _inner = inner;
        _logger = logger;

        Meter meter = meterFactory.Create(METER_NAME);
        _requestDuration = meter.CreateHistogram<double>(
            "ai_request_duration_seconds", unit: "s",
            description: "AI request latency end-to-end");
        _inputTokens = meter.CreateCounter<long>(
            "ai_input_tokens_total", unit: "tokens",
            description: "Cumulative input tokens billed by AI provider");
        _outputTokens = meter.CreateCounter<long>(
            "ai_output_tokens_total", unit: "tokens",
            description: "Cumulative output tokens billed by AI provider");
        _requests = meter.CreateCounter<long>(
            "ai_requests_total",
            description: "AI request count");
    }

    public Task<Result<AiBudgetAnalysis, Error>> AnalyzeAsync(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default)
        => _inner.AnalyzeAsync(request, cancellationToken);

    public async Task<Result<AiGenerationResult<T>, Error>> GenerateAsync<T>(
        AiGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        string model = request.Model;

        Result<AiGenerationResult<T>, Error> result =
            await _inner.GenerateAsync<T>(request, cancellationToken);

        double elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        string status = result.IsSuccess ? "ok" : "error";

        _requestDuration.Record(elapsed,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status));

        _requests.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status),
            new KeyValuePair<string, object?>(
                "finish_reason",
                result.IsSuccess ? result.Value.FinishReason.ToString() : "error"));

        if (result.IsSuccess && result.Value.Usage is { } usage)
        {
            _inputTokens.Add(usage.InputTokens,
                new KeyValuePair<string, object?>("model", model));
            _outputTokens.Add(usage.OutputTokens,
                new KeyValuePair<string, object?>("model", model));

            _logger.LogInformation(
                "AI {Model} ok in {Elapsed:F2}s — input:{InputTokens} output:{OutputTokens} reason:{FinishReason}",
                model, elapsed, usage.InputTokens, usage.OutputTokens, result.Value.FinishReason);
        }
        else if (result.IsFailure)
        {
            _logger.LogWarning(
                "AI {Model} failed in {Elapsed:F2}s: {ErrorCode} — {ErrorMessage}",
                model, elapsed,
                result.Error.Messages.Count > 0 ? result.Error.Messages[0].Code : "unknown",
                result.Error.GetMessage());
        }

        return result;
    }
}

/// <summary>
///     Аналогичный декоратор для <see cref="IAiTranscriptionClient"/> — STT pipeline
///     отдельно, потому что биллится в минутах аудио, не в токенах. Метрики:
///     <list type="bullet">
///         <item><c>ai_transcription_duration_seconds</c> (histogram, tags: model)</item>
///         <item><c>ai_transcription_audio_minutes_total</c> (counter, tags: model) —
///             грубый суммарный объём аудио в минутах для cost-tracking</item>
///         <item><c>ai_transcription_requests_total</c> (counter, tags: model, status)</item>
///     </list>
/// </summary>
public sealed class MeteredAiTranscriptionClient : IAiTranscriptionClient
{
    private const string METER_NAME = "EducationPlatform.AI";

    private readonly IAiTranscriptionClient _inner;
    private readonly ILogger<MeteredAiTranscriptionClient> _logger;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _audioBytes;
    private readonly Counter<long> _requests;

    public MeteredAiTranscriptionClient(
        IAiTranscriptionClient inner,
        IMeterFactory meterFactory,
        ILogger<MeteredAiTranscriptionClient> logger)
    {
        _inner = inner;
        _logger = logger;

        Meter meter = meterFactory.Create(METER_NAME);
        _requestDuration = meter.CreateHistogram<double>(
            "ai_transcription_duration_seconds", unit: "s",
            description: "STT request latency");
        _audioBytes = meter.CreateCounter<long>(
            "ai_transcription_audio_bytes_total", unit: "By",
            description: "Cumulative audio bytes sent to STT provider (proxy for cost)");
        _requests = meter.CreateCounter<long>(
            "ai_transcription_requests_total",
            description: "STT request count");
    }

    public async Task<Result<AiTranscriptionResult, Error>> TranscribeAsync(
        AiTranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        string model = request.Model;
        long bytes = request.AudioBytes.Count;

        Result<AiTranscriptionResult, Error> result =
            await _inner.TranscribeAsync(request, cancellationToken);

        double elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        string status = result.IsSuccess ? "ok" : "error";

        _requestDuration.Record(elapsed,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status));
        _audioBytes.Add(bytes,
            new KeyValuePair<string, object?>("model", model));
        _requests.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status));

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "STT {Model} ok in {Elapsed:F2}s — audio:{Bytes}B segments:{SegmentCount}",
                model, elapsed, bytes, result.Value.Segments.Count);
        }
        else
        {
            _logger.LogWarning(
                "STT {Model} failed in {Elapsed:F2}s: {ErrorCode} — {ErrorMessage}",
                model, elapsed,
                result.Error.Messages.Count > 0 ? result.Error.Messages[0].Code : "unknown",
                result.Error.GetMessage());
        }

        return result;
    }
}
