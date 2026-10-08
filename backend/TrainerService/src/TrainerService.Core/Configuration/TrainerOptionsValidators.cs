using Microsoft.Extensions.Options;

namespace TrainerService.Core.Configuration;

public sealed class TrainerOptionsValidator : IValidateOptions<TrainerOptions>
{
    public ValidateOptionsResult Validate(string? name, TrainerOptions options) =>
        options.FreeSamplePercent is >= 1 and <= 100
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Trainer:FreeSamplePercent must be between 1 and 100.");
}

public sealed class TrainerAiOptionsValidator : IValidateOptions<TrainerAiOptions>
{
    private const long ABSOLUTE_MAX_AUDIO_BYTES = 25 * 1024 * 1024;
    private readonly bool _requireFiniteProductionLimits;

    public TrainerAiOptionsValidator(bool requireFiniteProductionLimits) =>
        _requireFiniteProductionLimits = requireFiniteProductionLimits;

    public ValidateOptionsResult Validate(string? name, TrainerAiOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Transcription.Model))
            failures.Add("TrainerAI:Transcription:Model is required.");
        if (string.IsNullOrWhiteSpace(options.Grading.Model))
            failures.Add("TrainerAI:Grading:Model is required.");
        if (options.Grading.Temperature is < 0 or > 2)
            failures.Add("TrainerAI:Grading:Temperature must be between 0 and 2.");
        if (options.Grading.MaxOutputTokens is < 1 or > 32_768)
            failures.Add("TrainerAI:Grading:MaxOutputTokens must be between 1 and 32768.");
        if (options.Grading.TimeoutSeconds is < 1 or > 900)
            failures.Add("TrainerAI:Grading:TimeoutSeconds must be between 1 and 900.");
        if (options.MaxAudioBytes is < 1 or > ABSOLUTE_MAX_AUDIO_BYTES)
            failures.Add("TrainerAI:MaxAudioBytes must be between 1 byte and 25 MiB.");
        if (options.MaxVoiceAnswerSeconds is < 1 or > 1_800)
            failures.Add("TrainerAI:MaxVoiceAnswerSeconds must be between 1 and 1800.");
        if (options.OpenGradeRateLimitPerMinute is < 0 or > 1_000)
            failures.Add("TrainerAI:OpenGradeRateLimitPerMinute must be between 0 and 1000.");
        if (options.Pricing.TranscriptionPerMinuteRub < 0)
            failures.Add("TrainerAI:Pricing:TranscriptionPerMinuteRub cannot be negative.");

        foreach ((string model, TrainerAiModelPrice price) in options.Pricing.Models)
        {
            if (string.IsNullOrWhiteSpace(model)
                || price.InputPerMillionRub < 0
                || price.OutputPerMillionRub < 0)
            {
                failures.Add("TrainerAI model pricing requires a model name and non-negative prices.");
                break;
            }
        }

        ValidateTier("Free", options.Limits.Free, requirePositive: false, failures);
        ValidateTier("Pro", options.Limits.Pro, _requireFiniteProductionLimits, failures);

        if (_requireFiniteProductionLimits && options.OpenGradeRateLimitPerMinute == 0)
            failures.Add("TrainerAI:OpenGradeRateLimitPerMinute must be enabled in production.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateTier(
        string tierName,
        TrainerAiTierLimits tier,
        bool requirePositive,
        ICollection<string> failures)
    {
        int minimum = requirePositive ? 1 : 0;
        if (tier.OpenGradesPerDay < minimum
            || tier.VoiceMinutesPerMonth < minimum
            || tier.MockPerMonth < minimum)
        {
            failures.Add(
                $"TrainerAI:Limits:{tierName} values must be "
                + (requirePositive ? "positive in production." : "non-negative."));
        }
    }
}
