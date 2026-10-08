namespace AccessService.Contracts.Plans.Dtos;

/// <summary>
/// Plan read DTO — full projection used by author-side and public catalog endpoints.
/// </summary>
/// <param name="Capabilities">
/// Список capability-имён из <c>PlanCapabilities</c> bitmask:
/// <c>VIEW_MATERIALS, SUBMIT_ISSUES, CODE_REVIEW, COMMUNITY_ACCESS, LIVE_CALLS, JOB_SUPPORT</c>.
/// Frontend рендерит ✓/✗ список из этого массива.
/// </param>
/// <param name="IsHighlighted">Бейдж «ХИТ» / визуально выделенный план на pricing-странице.</param>
/// <param name="GithubOrgSlug">
/// GitHub-org slug. Юзер, состоящий в этом org, при логине через GitHub получает
/// PlanGrant автоматически. <c>null</c> = привязки нет.
/// </param>
/// <param name="DiscountPercent">Процент акционной скидки 1..99; <c>null</c> = акции нет.</param>
/// <param name="DiscountStartsAt">Начало окна акции (UTC).</param>
/// <param name="DiscountEndsAt">Конец окна акции (UTC); после него цена возвращается к обычной.</param>
/// <param name="PromotionActive">Активна ли акция прямо сейчас (вычислено на сервере).</param>
/// <param name="EffectivePriceCents">Цена с учётом активной акции; равна <c>PriceCents</c> если акции нет.</param>
public sealed record PlanDto(
    Guid Id,
    Guid AuthorId,
    string Tier,
    string OfferType,
    string Slug,
    string DisplayName,
    string ShortDescription,
    string LongDescription,
    Guid? CoverFileId,
    IReadOnlyList<string> Features,
    long? PriceCents,
    string Currency,
    int? DiscountPercent,
    DateTimeOffset? DiscountStartsAt,
    DateTimeOffset? DiscountEndsAt,
    bool PromotionActive,
    long? EffectivePriceCents,
    IReadOnlyList<Guid> CourseIds,
    bool IncludesFutureContent,
    int? TrialDurationDays,
    IReadOnlyList<string> Capabilities,
    bool IsHighlighted,
    string TermKind,
    int? TermRecurringDays,
    bool IsPublic,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt,
    string? GithubOrgSlug);
