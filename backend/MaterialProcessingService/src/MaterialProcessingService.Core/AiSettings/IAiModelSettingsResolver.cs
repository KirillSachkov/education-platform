namespace MaterialProcessingService.Core.AiSettings;

/// <summary>
///     Возвращает effective AI-настройки: предпочитает БД-override, если есть; иначе config-default.
///     Кэширует результат, чтобы каждый job не дёргал БД.
/// </summary>
public interface IAiModelSettingsResolver
{
    Task<EffectiveAiModelSettings> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Сбросить кэш (вызывается из PUT-handler'а после сохранения).
    /// </summary>
    void Invalidate();
}

public sealed record EffectiveAiModelSettings(
    EffectiveAiModelSlot SpeechToText,
    EffectiveAiModelSlot TimecodeGeneration,
    EffectiveAiModelSlot ContentGeneration,
    bool AutoProcessVideosEnabled,
    AiModelSettingsSource Source,
    DateTime? UpdatedAt,
    Guid? UpdatedByUserId);

/// <summary>
///     Provider — имя AI-провайдера (<c>aitunnel</c>, <c>polza</c>, ...) из
///     <c>AI:Providers:*</c>. Provider живёт только в appsettings, не в БД —
///     admin UI не имеет UI для смены провайдера, только модели. Пустая
///     строка — фабрика возьмёт default-провайдера.
/// </summary>
public sealed record EffectiveAiModelSlot(
    string Provider,
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds);

public enum AiModelSettingsSource
{
    CONFIG = 0,
    DATABASE = 1,
}
