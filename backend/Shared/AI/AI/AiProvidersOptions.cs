namespace Shared.AI;

/// <summary>
///     Multi-provider AI configuration. Каждый провайдер — отдельная запись в
///     <see cref="Providers"/> словаре, ключ — short name (например, <c>aitunnel</c>,
///     <c>polza</c>), значение — <see cref="AiOptions"/> с собственным
///     <c>BaseUrl</c>/<c>ApiKey</c>/<c>Kind</c>.
///     <para>
///     Service-specific конфиг (например, <c>VideoProcessingAI</c>) ссылается
///     на провайдеров через <c>Provider</c> поле в каждом slot'е. Это даёт
///     гибкость: STT может ходить на один агрегатор, LLM — на другой.
///     </para>
///     <para>
///     Legacy backward-compat: если <see cref="Providers"/> пустой, верхние поля
///     (<see cref="Kind"/>, <see cref="ApiKey"/>, <see cref="BaseUrl"/>,
///     <see cref="TimeoutSeconds"/>) формируют единственного провайдера с именем
///     <see cref="DEFAULT_PROVIDER_NAME"/> — это сохраняет совместимость со старыми
///     конфигами вида <c>AI: { Kind: ..., BaseUrl: ... }</c>.
///     </para>
/// </summary>
public sealed class AiProvidersOptions
{
    public const string SECTION_NAME = "AI";
    public const string DEFAULT_PROVIDER_NAME = "default";

    /// <summary>Имя провайдера, который используется когда slot не указал свой.</summary>
    public string Default { get; set; } = DEFAULT_PROVIDER_NAME;

    // CA2227: read-only collection — config-binder use Add() в существующий dict.
    public Dictionary<string, AiOptions> Providers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // --- Legacy single-provider fields (используются если Providers пустой) ---

    public string Kind { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public int? TimeoutSeconds { get; set; }

    public bool SendProviderRoutingHints { get; set; }

    /// <summary>
    ///     Возвращает effective Dictionary провайдеров после применения legacy-fallback'а.
    ///     Если <see cref="Providers"/> пустой и заданы верхние поля — собирает
    ///     один провайдер с именем <see cref="Default"/> (default: <c>default</c>).
    ///     Если оба пустые — возвращает пустой словарь (вызовет validation failure).
    /// </summary>
    public IReadOnlyDictionary<string, AiOptions> GetEffectiveProviders()
    {
        if (Providers.Count > 0)
            return Providers;

        if (string.IsNullOrWhiteSpace(Kind) && string.IsNullOrWhiteSpace(BaseUrl))
            return new Dictionary<string, AiOptions>(StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, AiOptions>(StringComparer.OrdinalIgnoreCase)
        {
            [Default] = new AiOptions
            {
                Kind = Kind,
                ApiKey = ApiKey,
                BaseUrl = BaseUrl,
                TimeoutSeconds = TimeoutSeconds,
                SendProviderRoutingHints = SendProviderRoutingHints,
            },
        };
    }
}
