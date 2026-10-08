using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shared.AI;
using TrainerService.Core.Configuration;
using TrainerService.Core.Grading;

namespace TrainerService.IntegrationTests.Features.AiUsageLedgerFeature;

/// <summary>
///     Pure unit tests for <see cref="AiUsageCostCalculator"/> (#614 C1): micro-ruble cost from token
///     usage + per-model pricing, null usage → 0, unknown model → 0. No DB / no DI container.
/// </summary>
public sealed class AiUsageCostCalculatorTests
{
    private static AiUsageCostCalculator Build(params (string Model, decimal In, decimal Out)[] prices)
    {
        TrainerAiOptions options = new();
        options.Pricing.Models.Clear();
        foreach ((string model, decimal input, decimal output) in prices)
            options.Pricing.Models[model] = new TrainerAiModelPrice { InputPerMillionRub = input, OutputPerMillionRub = output };

        return new AiUsageCostCalculator(Options.Create(options), NullLogger<AiUsageCostCalculator>.Instance);
    }

    [Fact]
    public void Cost_computes_micro_rub_from_tokens_and_per_million_price()
    {
        // gpt-4.1-mini @ ₽77 input / ₽307 output per 1M. 1000 in / 500 out:
        //   in  = 1000/1e6 * 77  = 0.077  ₽
        //   out =  500/1e6 * 307 = 0.1535 ₽
        //   total = 0.2305 ₽ → 230_500 micro-rub.
        AiUsageCostCalculator calc = Build(("gpt-4.1-mini", 77m, 307m));

        long cost = calc.Cost("gpt-4.1-mini", new AiUsage(InputTokens: 1000, OutputTokens: 500, TotalTokens: 1500));

        Assert.Equal(230_500L, cost);
    }

    [Fact]
    public void Cost_is_model_case_insensitive()
    {
        AiUsageCostCalculator calc = Build(("gpt-4.1-mini", 77m, 307m));

        long cost = calc.Cost("GPT-4.1-Mini", new AiUsage(InputTokens: 1000, OutputTokens: 500, TotalTokens: 1500));

        Assert.Equal(230_500L, cost);
    }

    [Fact]
    public void Cost_is_zero_for_null_usage()
    {
        AiUsageCostCalculator calc = Build(("gpt-4.1-mini", 77m, 307m));

        Assert.Equal(0L, calc.Cost("gpt-4.1-mini", usage: null));
    }

    [Fact]
    public void Cost_is_zero_for_unknown_model_without_throwing()
    {
        AiUsageCostCalculator calc = Build(("gpt-4.1-mini", 77m, 307m));

        long cost = calc.Cost("some-unpriced-model", new AiUsage(InputTokens: 1000, OutputTokens: 500, TotalTokens: 1500));

        Assert.Equal(0L, cost);
    }

    [Fact]
    public void Cost_rounds_tiny_per_call_amounts_to_micro_rub_not_zero()
    {
        // A 10-token grade at ₽77/1M input → 770 micro-rub (₽0.00077) — kept, not rounded to 0.
        AiUsageCostCalculator calc = Build(("gpt-4.1-mini", 77m, 307m));

        long cost = calc.Cost("gpt-4.1-mini", new AiUsage(InputTokens: 10, OutputTokens: 0, TotalTokens: 10));

        Assert.Equal(770L, cost);
    }
}
