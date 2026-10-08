using System.Diagnostics;
using System.Diagnostics.Metrics;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace Shared.AI;

/// <summary>
///     Decorator над любым <see cref="IAiEmbeddingsClient"/>, который пишет cost/usage
///     метрики в Meter <c>EducationPlatform.AI</c>:
///     <list type="bullet">
///         <item><c>ai_embedding_request_duration_seconds</c> (histogram, tags: model, status)</item>
///         <item><c>ai_embedding_input_tokens_total</c> (counter, tags: model)</item>
///         <item><c>ai_embedding_requests_total</c> (counter, tags: model, status)</item>
///         <item><c>ai_embedding_inputs_total</c> (counter, tags: model) — суммарное число
///             embed'ленных строк (proxy для cost'а отдельных батчей).</item>
///     </list>
///     Регистрируется автоматически через <c>AddAi(...)</c> если в DI присутствует
///     <see cref="IAiEmbeddingsClient"/>.
/// </summary>
public sealed class MeteredAiEmbeddingsClient : IAiEmbeddingsClient
{
    private const string METER_NAME = "EducationPlatform.AI";

    private readonly IAiEmbeddingsClient _inner;
    private readonly ILogger<MeteredAiEmbeddingsClient> _logger;
    private readonly Histogram<double> _requestDuration;
    private readonly Counter<long> _inputTokens;
    private readonly Counter<long> _inputs;
    private readonly Counter<long> _requests;

    public MeteredAiEmbeddingsClient(
        IAiEmbeddingsClient inner,
        IMeterFactory meterFactory,
        ILogger<MeteredAiEmbeddingsClient> logger)
    {
        _inner = inner;
        _logger = logger;

        Meter meter = meterFactory.Create(METER_NAME);
        _requestDuration = meter.CreateHistogram<double>(
            "ai_embedding_request_duration_seconds", unit: "s",
            description: "Embedding request latency end-to-end");
        _inputTokens = meter.CreateCounter<long>(
            "ai_embedding_input_tokens_total", unit: "tokens",
            description: "Cumulative input tokens billed by embedding provider");
        _inputs = meter.CreateCounter<long>(
            "ai_embedding_inputs_total",
            description: "Cumulative number of strings sent for embedding (batch size proxy)");
        _requests = meter.CreateCounter<long>(
            "ai_embedding_requests_total",
            description: "Embedding request count");
    }

    public async Task<Result<AiEmbeddingsResult, Error>> EmbedAsync(
        AiEmbeddingsRequest request,
        CancellationToken cancellationToken = default)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        string model = request.Model;
        int batchSize = request.Inputs.Count;

        Result<AiEmbeddingsResult, Error> result = await _inner.EmbedAsync(request, cancellationToken);

        double elapsed = Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;
        string status = result.IsSuccess ? "ok" : "error";

        _requestDuration.Record(elapsed,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status));
        _requests.Add(1,
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("status", status));
        _inputs.Add(batchSize,
            new KeyValuePair<string, object?>("model", model));

        if (result.IsSuccess)
        {
            _inputTokens.Add(result.Value.InputTokens,
                new KeyValuePair<string, object?>("model", model));

            _logger.LogInformation(
                "Embedding {Model} ok in {Elapsed:F2}s — inputs:{BatchSize} tokens:{InputTokens}",
                model, elapsed, batchSize, result.Value.InputTokens);
        }
        else
        {
            _logger.LogWarning(
                "Embedding {Model} failed in {Elapsed:F2}s: {ErrorCode} — {ErrorMessage}",
                model, elapsed,
                result.Error.Messages.Count > 0 ? result.Error.Messages[0].Code : "unknown",
                result.Error.GetMessage());
        }

        return result;
    }
}
