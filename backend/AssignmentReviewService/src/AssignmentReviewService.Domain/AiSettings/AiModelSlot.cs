namespace AssignmentReviewService.Domain.AiSettings;

/// <summary>
///     Value object: настройки одной AI-задачи (модель + temperature + лимиты).
///     Mirror <c>MaterialProcessingService.Domain.AiSettings.AiModelSlot</c>.
/// </summary>
public sealed class AiModelSlot
{
    public const int MAX_MODEL_LENGTH = 100;
    public const int MAX_OUTPUT_TOKENS = 100_000;
    public const int MAX_TIMEOUT_SECONDS = 3600;

    private AiModelSlot() { }

    private AiModelSlot(string model, double? temperature, int? maxOutputTokens, int? timeoutSeconds)
    {
        Model = model;
        Temperature = temperature;
        MaxOutputTokens = maxOutputTokens;
        TimeoutSeconds = timeoutSeconds;
    }

    public string Model { get; private set; } = string.Empty;

    public double? Temperature { get; private set; }

    public int? MaxOutputTokens { get; private set; }

    public int? TimeoutSeconds { get; private set; }

    public static Result<AiModelSlot, Error> Create(
        string model,
        double? temperature,
        int? maxOutputTokens,
        int? timeoutSeconds)
    {
        if (string.IsNullOrWhiteSpace(model))
            return Error.Validation("ai.model.empty", "Имя модели обязательно");

        string trimmed = model.Trim();
        if (trimmed.Length > MAX_MODEL_LENGTH)
            return Error.Validation(
                "ai.model.too_long",
                $"Имя модели не может превышать {MAX_MODEL_LENGTH} символов");

        if (temperature is < 0 or > 2)
            return Error.Validation(
                "ai.temperature.out_of_range",
                "Temperature должен быть в диапазоне [0, 2]");

        if (maxOutputTokens is <= 0 or > MAX_OUTPUT_TOKENS)
            return Error.Validation(
                "ai.max_output_tokens.invalid",
                $"MaxOutputTokens должен быть в диапазоне [1, {MAX_OUTPUT_TOKENS}]");

        if (timeoutSeconds is <= 0 or > MAX_TIMEOUT_SECONDS)
            return Error.Validation(
                "ai.timeout_seconds.invalid",
                $"TimeoutSeconds должен быть в диапазоне [1, {MAX_TIMEOUT_SECONDS}]");

        return new AiModelSlot(trimmed, temperature, maxOutputTokens, timeoutSeconds);
    }
}
