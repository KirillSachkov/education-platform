namespace EducationContentService.Contracts.Courses;

public sealed record CourseCatalogDto(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    string Kind,
    Guid? ImageId,
    string? ImageUrl,
    bool HasFreeContent,
    bool IsNew,
    DateTime CreatedAt,
    CoursePricingBlock? Pricing = null,
    // IsAccessible — у вызывающего уже есть доступ к курсу (роль автора/админа, план, enrollment).
    // Штампуется per-request пост-кешем; аноним → false.
    bool IsAccessible = false,
    // ShowInFullAccess — показывать ли курс в showcase «Полный доступ» на /pricing.
    // Display-only, на доступ не влияет. Default true (см. Course.ShowInFullAccess).
    bool ShowInFullAccess = true,
    // AuthorDisplayName / AuthorAvatarUrl — авторский кредит карточки (#569, model A).
    // Обогащается на бэке через AuthService (display name) + FileService (avatar URL).
    // Name — best-effort: null если AuthService недоступен; avatar — null если нет аватара.
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null);

/// <summary>
///     Pricing snapshot for a course's active+public COURSE-tier plan, served by AccessService.
///     Null when no plan is bound to the course (or AccessService is degraded — soft-fail).
///     <para>
///     <paramref name="PriceCents"/> — обычная (list) цена. <paramref name="EffectivePriceCents"/> —
///     цена с учётом активной акции (равна list если акции нет), считается в read-time.
///     <paramref name="DiscountPercent"/> / <paramref name="DiscountEndsAt"/> заполнены только
///     когда <paramref name="PromotionActive"/> = true (для бейджа «−N%» и хинта «до DD»).
///     </para>
/// </summary>
public sealed record CoursePricingBlock(
    Guid PlanId,
    long PriceCents,
    string Currency,
    string PlanSlug,
    long EffectivePriceCents,
    int? DiscountPercent,
    DateTimeOffset? DiscountEndsAt,
    bool PromotionActive);
