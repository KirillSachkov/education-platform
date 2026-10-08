namespace EducationContentService.Core.Features.Plans;

/// <summary>
/// Per-course pricing snapshot cached from AccessService. Carries the RAW акция-окно
/// (percent + dates) — effective price / activity вычисляются в read-time (<see cref="EffectivePriceCents"/>),
/// поэтому кеш не зависит от времени и инвалидируется только при изменении акции автором.
/// </summary>
public sealed record CoursePricingDto(
    Guid PlanId,
    Guid AuthorId,
    Guid CourseId,
    string Slug,
    string DisplayName,
    long PriceCents,
    string Currency,
    int? DiscountPercent,
    DateTimeOffset? DiscountStartsAt,
    DateTimeOffset? DiscountEndsAt)
{
    /// <summary>Активна ли акция в момент <paramref name="now"/>. Зеркалит `Plan.IsPromotionActive`.</summary>
    public bool IsPromotionActive(DateTimeOffset now) =>
        DiscountPercent is >= 1 and <= 99
        && DiscountStartsAt is { } startsAt
        && DiscountEndsAt is { } endsAt
        && now >= startsAt
        && now < endsAt
        && PriceCents > 0;

    /// <summary>Цена с учётом активной акции. Целочисленный truncate — как на бэкенде/фронте.</summary>
    public long EffectivePriceCents(DateTimeOffset now) =>
        IsPromotionActive(now)
            ? Math.Max(0, PriceCents - (PriceCents * DiscountPercent!.Value / 100))
            : PriceCents;
}
