using TrainerService.Core.Configuration;

namespace TrainerService.IntegrationTests.Features.Configuration;

public sealed class TrainerOptionsValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Trainer_options_reject_invalid_free_sample_percent(int percent)
    {
        var result = new TrainerOptionsValidator().Validate(
            null,
            new TrainerOptions { FreeSamplePercent = percent });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Production_ai_options_reject_unlimited_pro_limits()
    {
        var options = new TrainerAiOptions();
        options.Limits.Pro.OpenGradesPerDay = 0;

        var result = new TrainerAiOptionsValidator(requireFiniteProductionLimits: true)
            .Validate(null, options);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Ai_options_reject_negative_pricing_and_oversized_audio_limit()
    {
        var options = new TrainerAiOptions { MaxAudioBytes = 26 * 1024 * 1024 };
        options.Pricing.TranscriptionPerMinuteRub = -1;

        var result = new TrainerAiOptionsValidator(requireFiniteProductionLimits: false)
            .Validate(null, options);

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
    }
}
