namespace AccessService.Contracts.TrainerPro;

/// <summary>
/// Админский снимок оффер-варианта тренажёра (#674). Зеркалит <see cref="TrainerProOfferDto"/>
/// плюс статус-флаги (<see cref="IsPublic"/> / <see cref="IsActive"/> / <see cref="ArchivedAt"/>),
/// чтобы автор видел и опубликованные, и черновые варианты на странице управления оффером.
/// </summary>
public sealed record TrainerProOfferAdminDto(
    Guid Id,
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
    int? RecurringIntervalDays,
    IReadOnlyList<string> Capabilities,
    bool IsHighlighted,
    bool IsPublic,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ArchivedAt);
