namespace MaterialProcessingService.Core.Configuration;

/// <summary>
///     Глобальный kill-switch для AI-pipeline'а (issue #107).
///     Отключение через env var <c>AIPIPELINE__ENABLED=false</c> или
///     <c>"AiPipeline": { "Enabled": false }</c> в appsettings.
///     При выключении все enqueue-эндпоинты возвращают <c>ai.pipeline.disabled</c>;
///     активные фоновые job'ы продолжают работать (force-stop отдельный сценарий).
/// </summary>
public sealed class AiPipelineFeatureFlags
{
    public const string SECTION_NAME = "AiPipeline";

    public bool Enabled { get; set; } = true;
}
