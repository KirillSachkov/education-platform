using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Core.Features.Reviews;
using AssignmentReviewService.Domain.AiSettings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace AssignmentReviewService.Core.AiSettings;

internal sealed class AssignmentReviewAiModelSettingsResolver : IAssignmentReviewAiModelSettingsResolver
{
    internal const string CACHE_KEY = "assignment_review.ai_model_settings.singleton";

    /// <summary>
    ///     Redis pub/sub channel для cross-replica cache invalidation. Когда
    ///     admin меняет настройки через PUT, мы публикуем сюда — все реплики
    ///     (включая нашу — она тоже подписана) дропают свой L1 кэш и при
    ///     следующем чтении грузят свежий singleton из БД. Без этого при N>1
    ///     replicas другие pod'ы держат stale-настройки до истечения
    ///     <see cref="CACHE_TTL"/>.
    /// </summary>
    internal static readonly RedisChannel INVALIDATION_CHANNEL =
        RedisChannel.Literal("assignment_review:ai-settings:invalidated");

    private static readonly TimeSpan CACHE_TTL = TimeSpan.FromSeconds(60);

    private readonly IAiModelSettingsRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly IOptions<AssignmentReviewAiOptions> _options;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<AssignmentReviewAiModelSettingsResolver> _logger;

    public AssignmentReviewAiModelSettingsResolver(
        IAiModelSettingsRepository repository,
        IMemoryCache cache,
        IOptions<AssignmentReviewAiOptions> options,
        ILogger<AssignmentReviewAiModelSettingsResolver> logger,
        IConnectionMultiplexer? redis = null)
    {
        _repository = repository;
        _cache = cache;
        _options = options;
        _logger = logger;
        _redis = redis;
    }

    public async Task<EffectiveSlot> ResolveReviewerAsync(string? modelOverride, CancellationToken ct = default)
    {
        AssignmentReviewAiSlot config = _options.Value.Reviewer;
        AiModelSlot? db = (await TryLoadSettingsAsync(ct))?.Reviewer;
        return Resolve(modelOverride, db, config);
    }

    public async Task<string> ResolveReviewerBasePromptAsync(CancellationToken ct = default)
    {
        string? db = (await TryLoadSettingsAsync(ct))?.ReviewerBasePrompt;
        return string.IsNullOrWhiteSpace(db)
            ? _options.Value.ReviewerBasePrompt
            : db;
    }

    public async Task<bool> ResolveReviewEnabledAsync(CancellationToken ct = default)
    {
        AiModelSettings? db = await TryLoadSettingsAsync(ct);
        return db?.ReviewEnabled ?? _options.Value.ReviewEnabled;
    }

    public async Task<bool> ResolveRepoContextEnabledAsync(CancellationToken ct = default)
    {
        AiModelSettings? db = await TryLoadSettingsAsync(ct);
        return db?.RepoContextEnabled ?? _options.Value.RepoContext.Enabled;
    }

    public void InvalidateCache()
    {
        _cache.Remove(CACHE_KEY);

        // Cross-replica invalidate через Redis pub/sub. Fire-and-forget —
        // если Redis недоступен, другие реплики увидят свежие значения
        // через CACHE_TTL (60s); локальная реплика уже invalidated выше.
        if (_redis is not null)
        {
            _ = PublishInvalidationAsync();
        }
    }

    private async Task PublishInvalidationAsync()
    {
        try
        {
            await _redis!.GetSubscriber().PublishAsync(INVALIDATION_CHANNEL, RedisValue.EmptyString);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to publish AI settings invalidation to Redis (local cache already cleared)");
        }
    }

    private async Task<AiModelSettings?> TryLoadSettingsAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue<AiModelSettings?>(CACHE_KEY, out AiModelSettings? cached))
            return cached;

        AiModelSettings? loaded = await _repository.GetSingletonAsync(ct);
        _cache.Set(CACHE_KEY, loaded, CACHE_TTL);
        return loaded;
    }

    private static EffectiveSlot Resolve(
        string? modelOverride,
        AiModelSlot? db,
        AssignmentReviewAiSlot config)
    {
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            return new EffectiveSlot(
                modelOverride.Trim(),
                db?.Temperature ?? config.Temperature,
                db?.MaxOutputTokens ?? config.MaxOutputTokens,
                db?.TimeoutSeconds ?? config.TimeoutSeconds,
                AiModelSettingsSource.OVERRIDE);
        }

        if (db is not null)
        {
            return new EffectiveSlot(
                db.Model,
                db.Temperature,
                db.MaxOutputTokens,
                db.TimeoutSeconds,
                AiModelSettingsSource.DATABASE);
        }

        return new EffectiveSlot(
            config.Model,
            config.Temperature,
            config.MaxOutputTokens,
            config.TimeoutSeconds,
            AiModelSettingsSource.CONFIG);
    }
}
