using Microsoft.Extensions.Options;
using MaterialProcessingService.Domain.AiSettings;

namespace MaterialProcessingService.Infrastructure.AI.Configuration;

internal sealed class VideoProcessingAiOptionsValidator : IValidateOptions<VideoProcessingAiOptions>
{
    public ValidateOptionsResult Validate(string? name, VideoProcessingAiOptions options) =>
        Validate(options);

    public static ValidateOptionsResult Validate(VideoProcessingAiOptions options)
    {
        ValidateOptionsResult speechToTextResult = ValidateRequiredModelOptions(
            options.SpeechToText,
            $"{VideoProcessingAiOptions.SECTION_NAME}:SpeechToText");
        if (speechToTextResult.Failed)
            return speechToTextResult;

        ValidateOptionsResult timecodesResult = ValidateRequiredModelOptions(
            options.TimecodeGeneration,
            $"{VideoProcessingAiOptions.SECTION_NAME}:TimecodeGeneration");
        if (timecodesResult.Failed)
            return timecodesResult;

        return ValidateRequiredModelOptions(
            options.ContentGeneration,
            $"{VideoProcessingAiOptions.SECTION_NAME}:ContentGeneration");
    }

    private static ValidateOptionsResult ValidateRequiredModelOptions(
        VideoProcessingAiModelOptions options,
        string sectionPath)
    {
        if (string.IsNullOrWhiteSpace(options.Model))
            return ValidateOptionsResult.Fail($"{sectionPath}:Model is required");

        if (options.Model.Length > AiModelSlot.MAX_MODEL_LENGTH)
            return ValidateOptionsResult.Fail(
                $"{sectionPath}:Model must not exceed {AiModelSlot.MAX_MODEL_LENGTH} characters");

        if (options.Temperature is < 0 or > 2)
            return ValidateOptionsResult.Fail($"{sectionPath}:Temperature must be between 0 and 2");

        if (options.MaxOutputTokens is <= 0)
            return ValidateOptionsResult.Fail($"{sectionPath}:MaxOutputTokens must be greater than 0");

        if (options.MaxOutputTokens is > AiModelSlot.MAX_OUTPUT_TOKENS)
            return ValidateOptionsResult.Fail(
                $"{sectionPath}:MaxOutputTokens must not exceed {AiModelSlot.MAX_OUTPUT_TOKENS}");

        if (options.TimeoutSeconds is <= 0)
            return ValidateOptionsResult.Fail($"{sectionPath}:TimeoutSeconds must be greater than 0");

        if (options.TimeoutSeconds is > AiModelSlot.MAX_TIMEOUT_SECONDS)
            return ValidateOptionsResult.Fail(
                $"{sectionPath}:TimeoutSeconds must not exceed {AiModelSlot.MAX_TIMEOUT_SECONDS}");

        return ValidateOptionsResult.Success;
    }
}
