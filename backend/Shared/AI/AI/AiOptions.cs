namespace Shared.AI;

public sealed class AiOptions
{
    public const string SECTION_NAME = "AI";

    public string Kind { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public int? TimeoutSeconds { get; set; }

    /// <summary>
    ///     Опциональный флаг — посылать ли в STT multipart форму extra-поле
    ///     <c>provider</c> с routing-хинтами (<c>allow_fallbacks=true</c>,
    ///     <c>sort=throughput</c>). Polza-specific extension: на Polza ускоряет
    ///     роутинг к up-провайдеру с лучшим throughput. Другие OpenAI-совместимые
    ///     прокси (AITunnel, OpenAI direct) либо игнорируют, либо могут 400'нуть
    ///     на неизвестное поле. Default = <c>false</c> (безопасный для всех);
    ///     для Polza явно выставить <c>true</c> в провайдер-конфиге.
    /// </summary>
    public bool SendProviderRoutingHints { get; set; }
}
