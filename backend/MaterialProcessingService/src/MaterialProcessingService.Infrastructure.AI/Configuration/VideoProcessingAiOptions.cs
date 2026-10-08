namespace MaterialProcessingService.Infrastructure.AI.Configuration;

internal sealed class VideoProcessingAiOptions
{
    public const string SECTION_NAME = "VideoProcessingAI";

    public VideoProcessingAiModelOptions SpeechToText { get; set; } = new();

    public VideoProcessingAiModelOptions TimecodeGeneration { get; set; } = new();

    public VideoProcessingAiModelOptions ContentGeneration { get; set; } = new();
}

internal sealed class VideoProcessingAiModelOptions
{
    public string Model { get; set; } = string.Empty;

    /// <summary>
    ///     Имя провайдера из <c>AI:Providers:*</c>. Пустая строка — берём default-провайдера.
    ///     Provider живёт только в config (appsettings), не в БД — admin UI меняет только
    ///     Model/Temperature/MaxOutputTokens/TimeoutSeconds. Чтобы переключить провайдера,
    ///     поправить appsettings.Production.json + restart.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    public double? Temperature { get; set; }

    public int? MaxOutputTokens { get; set; }

    public int? TimeoutSeconds { get; set; }

    /// <summary>
    ///     Возвращает копию options с переопределённой моделью. Используется handler'ами
    ///     при наличии admin model override на job'е — request factories принимают
    ///     model name через эти options, без модификации самих factories.
    ///     Provider сохраняется — модель-override не меняет канал.
    /// </summary>
    public VideoProcessingAiModelOptions WithModel(string? modelOverride)
    {
        if (string.IsNullOrWhiteSpace(modelOverride))
            return this;

        return new VideoProcessingAiModelOptions
        {
            Model = modelOverride.Trim(),
            Provider = this.Provider,
            Temperature = this.Temperature,
            MaxOutputTokens = this.MaxOutputTokens,
            TimeoutSeconds = this.TimeoutSeconds,
        };
    }
}
