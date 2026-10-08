namespace AccessService.Contracts.Plans.Requests;

/// <summary>
/// Установка акции на план. Все три поля обязательны: процент скидки 1..99 и окно дат.
/// Даты в UTC (ISO 8601 с offset'ом). <c>EndsAt</c> должен быть позже <c>StartsAt</c> и в будущем.
/// </summary>
public sealed record SetPromotionRequest(
    int DiscountPercent,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);
