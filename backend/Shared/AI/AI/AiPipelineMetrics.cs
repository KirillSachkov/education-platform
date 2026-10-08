using System.Diagnostics.Metrics;

namespace Shared.AI;

/// <summary>
///     Метрики защиты pipeline'а от truncation (issue #110).
///     Регистрируются в meter <c>EducationPlatform.AI</c> вместе с метриками <see cref="MeteredAiClient"/>.
/// </summary>
public sealed class AiPipelineMetrics
{
    private const string METER_NAME = "EducationPlatform.AI";

    private readonly Counter<long> _truncations;
    private readonly Counter<long> _chunkSplits;
    private readonly Histogram<int> _continuationIterations;

    public AiPipelineMetrics(IMeterFactory meterFactory)
    {
        Meter meter = meterFactory.Create(METER_NAME);

        _truncations = meter.CreateCounter<long>(
            "ai_truncations_total",
            description: "Сколько раз AI-вызов вернул finish_reason=length (или эквивалент для STT). "
                + "Tag job: STT|TIMECODES|CONTENT. Tag level: 0..N (для STT — глубина recursion'а; для chat — номер retry/continuation).");

        _chunkSplits = meter.CreateCounter<long>(
            "ai_chunk_splits_total",
            description: "Сколько раз STT-чанк пришлось делить пополам из-за truncation detection.");

        _continuationIterations = meter.CreateHistogram<int>(
            "ai_continuation_iterations",
            description: "Distribution фактического числа итераций continuation-loop'а в Content-генерации (0 = ушло за один заход).");
    }

    public void RecordTruncation(string job, int level) =>
        _truncations.Add(1,
            new KeyValuePair<string, object?>("job", job),
            new KeyValuePair<string, object?>("level", level));

    public void RecordChunkSplit() => _chunkSplits.Add(1);

    public void RecordContinuationIterations(int count) => _continuationIterations.Record(count);
}
