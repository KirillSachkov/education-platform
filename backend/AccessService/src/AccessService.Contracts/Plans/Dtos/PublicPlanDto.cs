namespace AccessService.Contracts.Plans.Dtos;

/// <summary>
/// Plan read DTO for public (anonymous) catalog endpoints.
/// Same shape as PlanDto for now. Kept separate so future "private fields"
/// (e.g., usage analytics, internal notes) can diverge without breaking
/// anonymous consumers.
/// </summary>
public sealed record PublicPlanDto(
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
    DateTimeOffset? DiscountEndsAt,
    bool PromotionActive,
    long? EffectivePriceCents,
    // CourseId — first course of the bundle, kept for backward-compat (#404). CourseIds — full bundle.
    Guid? CourseId,
    IReadOnlyList<Guid> CourseIds,
    bool IncludesFutureContent,
    int? TrialDurationDays,
    IReadOnlyList<string> Capabilities,
    bool IsHighlighted,
    // Срок действия плана. TermKind = LIFETIME | RECURRING; для RECURRING (подписка, #614)
    // TermRecurringDays несёт интервал автосписания (напр. 30) — карточка рендерит «₽X / мес».
    string TermKind,
    int? TermRecurringDays,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    /// <summary>
    /// Названия курсов плана для COURSES kind — populates `IncludedCourses`
    /// section в PlanCard. Null/empty для других kind'ов или если ECS lookup
    /// упал (handler soft-degrade'ится: возвращает план без titles, чтобы
    /// pricing-страница не ложилась).
    /// </summary>
    IReadOnlyList<PublicPlanCourseDto>? IncludedCourses = null);

/// <summary>
/// Публичный проекшн курса в составе плана (bundle-карточка, #404). <see cref="Slug"/>
/// даёт кликабельную ссылку на страницу курса, <see cref="Kind"/> — бейдж
/// COURSE/INTENSIVE/MARATHON. Всё безопасно для анонимного показа (catalog/landing).
/// </summary>
public sealed record PublicPlanCourseDto(Guid Id, string Title, string Slug, string Kind);
