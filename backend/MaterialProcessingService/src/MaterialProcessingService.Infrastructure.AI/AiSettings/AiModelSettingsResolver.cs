using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MaterialProcessingService.Core.AiSettings;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.AiSettings;
using MaterialProcessingService.Infrastructure.AI.Configuration;

namespace MaterialProcessingService.Infrastructure.AI.AiSettings;

/// <summary>
///     Single-pod: <see cref="IMemoryCache.Remove(object)"/> в <see cref="Invalidate"/> срабатывает мгновенно после PUT.
///     Multi-pod (если перейдём на горизонтальное масштабирование): TTL=60s означает 60s дрифта между подами.
///     В этом случае заменить <see cref="IMemoryCache"/> на <c>IDistributedCache</c> + pub/sub-инвалидацию через Redis.
/// </summary>
internal sealed class AiModelSettingsResolver : IAiModelSettingsResolver
{
    private const string CACHE_KEY = "material-processing.ai-model-settings.effective";
    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromSeconds(60);

    private readonly IAiModelSettingsRepository _repository;
    private readonly IOptionsMonitor<VideoProcessingAiOptions> _configOptions;
    private readonly IMemoryCache _cache;

    public AiModelSettingsResolver(
        IAiModelSettingsRepository repository,
        IOptionsMonitor<VideoProcessingAiOptions> configOptions,
        IMemoryCache cache)
    {
        _repository = repository;
        _configOptions = configOptions;
        _cache = cache;
    }

    public async Task<EffectiveAiModelSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CACHE_KEY, out EffectiveAiModelSettings? cached) && cached is not null)
            return cached;

        VideoProcessingAiOptions config = _configOptions.CurrentValue;
        AiModelSettings? db = await _repository.GetAsync(asNoTracking: true, cancellationToken);

        EffectiveAiModelSettings effective = db is null
            ? FromConfig(config)
            : FromDatabase(db, config);

        _cache.Set(CACHE_KEY, effective, CACHE_TTL);
        return effective;
    }

    public void Invalidate() => _cache.Remove(CACHE_KEY);

    private static EffectiveAiModelSettings FromConfig(VideoProcessingAiOptions config) => new(
        SpeechToText: ToEffectiveSlot(config.SpeechToText),
        TimecodeGeneration: ToEffectiveSlot(config.TimecodeGeneration),
        ContentGeneration: ToEffectiveSlot(config.ContentGeneration),
        // Нет DB-override → авто-обработка включена по умолчанию (issue #648).
        AutoProcessVideosEnabled: true,
        Source: AiModelSettingsSource.CONFIG,
        UpdatedAt: null,
        UpdatedByUserId: null);

    /// <summary>
    ///     Provider не хранится в БД — берём из config slot. Остальные поля
    ///     (Model/Temperature/MaxOutputTokens/TimeoutSeconds) — из БД (admin override).
    /// </summary>
    private static EffectiveAiModelSettings FromDatabase(
        AiModelSettings settings,
        VideoProcessingAiOptions config) => new(
            SpeechToText: MergeWithProvider(settings.SpeechToText, config.SpeechToText.Provider),
            TimecodeGeneration: MergeWithProvider(settings.TimecodeGeneration, config.TimecodeGeneration.Provider),
            ContentGeneration: MergeWithProvider(settings.ContentGeneration, config.ContentGeneration.Provider),
            AutoProcessVideosEnabled: settings.AutoProcessVideosEnabled,
            Source: AiModelSettingsSource.DATABASE,
            UpdatedAt: settings.UpdatedAt,
            UpdatedByUserId: settings.UpdatedByUserId);

    private static EffectiveAiModelSlot ToEffectiveSlot(VideoProcessingAiModelOptions slot) =>
        new(slot.Provider, slot.Model, slot.Temperature, slot.MaxOutputTokens, slot.TimeoutSeconds);

    private static EffectiveAiModelSlot MergeWithProvider(AiModelSlot dbSlot, string configProvider) =>
        new(configProvider, dbSlot.Model, dbSlot.Temperature, dbSlot.MaxOutputTokens, dbSlot.TimeoutSeconds);
}
