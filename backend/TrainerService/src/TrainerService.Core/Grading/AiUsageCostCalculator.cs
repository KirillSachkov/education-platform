using Microsoft.Extensions.Options;
using Shared.AI;
using TrainerService.Core.Configuration;

namespace TrainerService.Core.Grading;

/// <summary>
///     Считает стоимость одного AI-вызова в <b>микрорублях</b> (₽ × 1 000 000) из <see cref="AiUsage"/>
///     + прайсинга модели (#614 C1). Микрорубли — чтобы дешёвые per-call вызовы не схлопывались в 0 при
///     округлении до копеек. Неизвестная модель → 0 + warning (не бросает). Транскрипция без usage
///     (<see cref="AiUsage"/> = null, как у whisper-1) → 0.
/// </summary>
public sealed class AiUsageCostCalculator
{
    private const long MICRO_PER_RUB = 1_000_000L;

    private readonly IOptions<TrainerAiOptions> _options;
    private readonly ILogger<AiUsageCostCalculator> _logger;

    public AiUsageCostCalculator(IOptions<TrainerAiOptions> options, ILogger<AiUsageCostCalculator> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>
    ///     Стоимость вызова <paramref name="model"/> с потреблением <paramref name="usage"/> в микрорублях.
    ///     Формула: <c>round((in/1e6·inPrice + out/1e6·outPrice) · 1_000_000)</c>. <paramref name="usage"/>
    ///     null (STT без token-биллинга) → 0. Неизвестная модель → 0 + warning.
    /// </summary>
    public long Cost(string model, AiUsage? usage)
    {
        if (usage is null)
            return 0L;

        TrainerAiPricingOptions pricing = _options.Value.Pricing;
        if (!pricing.Models.TryGetValue(model, out TrainerAiModelPrice? price))
        {
            _logger.LogWarning(
                "No AI pricing configured for model {Model} — recording cost 0. Add it to TrainerAI:Pricing:Models.",
                model);
            return 0L;
        }

        decimal inputCostRub = usage.InputTokens / 1_000_000m * price.InputPerMillionRub;
        decimal outputCostRub = usage.OutputTokens / 1_000_000m * price.OutputPerMillionRub;
        decimal microRub = (inputCostRub + outputCostRub) * MICRO_PER_RUB;

        return (long)Math.Round(microRub, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    ///     ESTIMATES the cost of one transcription call in microrubles from the audio
    ///     <paramref name="durationSeconds"/> × the configured per-minute price (#614 C2). STT is in
    ///     practice token-billed, but <see cref="AiTranscriptionResult"/> carries no token usage — so
    ///     per-minute is the best available proxy (normalized provider <c>DurationSeconds</c>).
    ///     <paramref name="durationSeconds"/> &lt;= 0 → 0.
    /// </summary>
    public long TranscriptionCost(double durationSeconds)
    {
        if (durationSeconds <= 0)
            return 0L;

        decimal minutes = (decimal)durationSeconds / 60m;
        decimal microRub = minutes * _options.Value.Pricing.TranscriptionPerMinuteRub * MICRO_PER_RUB;

        return (long)Math.Round(microRub, MidpointRounding.AwayFromZero);
    }
}
