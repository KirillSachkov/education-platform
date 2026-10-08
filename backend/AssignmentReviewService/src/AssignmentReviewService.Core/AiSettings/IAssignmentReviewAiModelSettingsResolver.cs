using AssignmentReviewService.Core.Features.Reviews;

namespace AssignmentReviewService.Core.AiSettings;

/// <summary>
///     Phase 11 (#15): резолвит AI-настройки для запуска reviewer'а.
///     Источник правды (cascading):
///     <list type="number">
///         <item>per-job override (admin only, query-string на run-iteration)</item>
///         <item>singleton DB row <c>ai_model_settings</c></item>
///         <item>config <c>AssignmentReviewAI</c> (appsettings)</item>
///     </list>
///     IMemoryCache TTL=60s; <see cref="InvalidateCache"/> вызывается update-handler'ом.
/// </summary>
public interface IAssignmentReviewAiModelSettingsResolver
{
    Task<EffectiveSlot> ResolveReviewerAsync(string? modelOverride, CancellationToken ct = default);

    /// <summary>
    ///     Резолвит глобальный base review prompt (#15): DB-singleton'а
    ///     <c>reviewer_base_prompt</c> если задан, иначе config-дефолт
    ///     (<c>AssignmentReviewAI:ReviewerBasePrompt</c>).
    /// </summary>
    Task<string> ResolveReviewerBasePromptAsync(CancellationToken ct = default);

    /// <summary>
    ///     Резолвит платформенный тумблер AI-проверки PR (#355): DB-singleton'а
    ///     <c>review_enabled</c> если row есть, иначе config-дефолт
    ///     (<c>AssignmentReviewAI:ReviewEnabled</c>, по умолчанию <c>false</c>).
    /// </summary>
    Task<bool> ResolveReviewEnabledAsync(CancellationToken ct = default);

    /// <summary>
    ///     Резолвит тумблер дозапроса файлов репозитория (#798): DB-singleton'а
    ///     <c>repo_context_enabled</c> если row есть, иначе config-дефолт
    ///     (<c>AssignmentReviewAI:RepoContext:Enabled</c>, по умолчанию <c>false</c>).
    /// </summary>
    Task<bool> ResolveRepoContextEnabledAsync(CancellationToken ct = default);

    /// <summary>Сбросить cache; зовётся UpdateAiSettings handler'ом.</summary>
    void InvalidateCache();
}

/// <summary>
///     Effective slot — что реально пойдёт в LLM call. <see cref="Source"/>
///     показывает откуда значение пришло (для админ-UI).
/// </summary>
public sealed record EffectiveSlot(
    string Model,
    double? Temperature,
    int? MaxOutputTokens,
    int? TimeoutSeconds,
    AiModelSettingsSource Source)
{
    public AssignmentReviewAiSlot ToConfigSlot() => new()
    {
        Model = Model,
        Temperature = Temperature ?? 0.0,
        MaxOutputTokens = MaxOutputTokens ?? 4000,
        TimeoutSeconds = TimeoutSeconds ?? 300,
    };
}

public enum AiModelSettingsSource
{
    /// <summary>Значение взято из appsettings.json.</summary>
    CONFIG,

    /// <summary>Значение взято из БД-singleton'а (admin override).</summary>
    DATABASE,

    /// <summary>Значение пришло per-job override (admin только).</summary>
    OVERRIDE,
}
