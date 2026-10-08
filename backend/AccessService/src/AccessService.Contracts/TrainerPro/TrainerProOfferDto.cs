namespace AccessService.Contracts.TrainerPro;

/// <summary>
/// Публичный оффер-вариант подписки тренажёра (Trainer Pro, #674). Каждый вариант — отдельный
/// <c>Plan</c> (Scope=TRAINER, OfferType=TRAINER_PRO, recurring term), различающийся периодом
/// (<see cref="RecurringIntervalDays"/>) и ценой. Отдаётся анонимно из <c>GET /access/trainer-pro/offer</c>;
/// покупается через <c>POST /access/trainer-pro/orders</c> по <see cref="Id"/>. Не пересекается с
/// платформенным каталогом.
/// </summary>
public sealed record TrainerProOfferDto(
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
    DateTimeOffset? DiscountEndsAt,
    bool PromotionActive,
    long? EffectivePriceCents,
    // Интервал автосписания в днях (период подписки): 30 → «₽X / мес» на карточке.
    int? RecurringIntervalDays,
    IReadOnlyList<string> Capabilities,
    bool IsHighlighted,
    int DisplayOrder);
