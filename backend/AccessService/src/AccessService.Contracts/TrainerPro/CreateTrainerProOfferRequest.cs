namespace AccessService.Contracts.TrainerPro;

/// <summary>
/// Запрос на создание оффер-варианта подписки тренажёра (#674). Под капотом создаётся обычный
/// <c>Plan</c> tier=SUBSCRIPTION (домен форсит OfferType=TRAINER_PRO, capability TRAINER_PRO,
/// Scope=TRAINER) с периодическим сроком <see cref="RecurringIntervalDays"/>. При
/// <see cref="IsActive"/>=true вариант сразу публикуется (становится покупаемым). Автор не задаёт
/// tier/offerType — они залиты доменом.
/// </summary>
public sealed record CreateTrainerProOfferRequest(
    string Slug,
    string DisplayName,
    long PriceCents,
    int RecurringIntervalDays,
    string? ShortDescription = null,
    string? LongDescription = null,
    Guid? CoverFileId = null,
    IReadOnlyList<string>? Features = null,
    string? Currency = null,
    bool IsHighlighted = false,
    int? DisplayOrder = null,
    bool IsActive = true);
