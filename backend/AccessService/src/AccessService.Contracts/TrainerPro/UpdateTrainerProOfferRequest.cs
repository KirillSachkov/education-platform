namespace AccessService.Contracts.TrainerPro;

/// <summary>
/// Запрос на правку оффер-варианта тренажёра (#674). Все поля опциональны: <c>null</c> = не менять.
/// <see cref="IsActive"/> переключает покупаемость (publish/unpublish, не архив). Применяется только
/// к плану со Scope=TRAINER (иначе <c>trainer_pro.offer.scope_mismatch</c>).
/// </summary>
public sealed record UpdateTrainerProOfferRequest(
    long? PriceCents = null,
    string? Currency = null,
    string? DisplayName = null,
    string? ShortDescription = null,
    string? LongDescription = null,
    Guid? CoverFileId = null,
    IReadOnlyList<string>? Features = null,
    bool? IsHighlighted = null,
    int? DisplayOrder = null,
    bool? IsActive = null);
