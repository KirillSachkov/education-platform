namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Quote-расчёт upgrade-цены при покупке плана. Возвращается endpoint'ом
///     <c>GET /access/plans/{planId}/upgrade-quote/</c> и используется фронтом для
///     показа итоговой цены + breakdown'а скидки на pricing-странице.
/// </summary>
/// <param name="OriginalPriceCents">
///     Эффективная цена target-плана. Для апгрейда после paid trial может быть ниже текущей
///     цены плана: используется снимок базовой lifetime-цены на момент покупки месяца.
///     Null если у плана нет цены (план free / TBD).
/// </param>
/// <param name="CreditCents">
///     Сумма скидки = sum(price_paid_cents) неотозванных grants пользователя,
///     scope которых ⊆ scope(target). Никогда не превышает <c>OriginalPriceCents</c>.
/// </param>
/// <param name="FinalPriceCents">
///     <c>max(0, OriginalPriceCents - CreditCents)</c>. Null если original null.
/// </param>
/// <param name="IsOwned">
///     <c>true</c> если у пользователя уже есть ACTIVE grant на этот же
///     <c>target_plan</c>. Frontend покажет «У вас уже есть этот план» + скрывает
///     payment кнопку.
/// </param>
/// <param name="Sources">
///     Breakdown — какие grants дали credit (для UI «–25 000 ₽ за курс React»).
/// </param>
public sealed record UpgradeQuoteDto(
    long? OriginalPriceCents,
    long CreditCents,
    long? FinalPriceCents,
    bool IsOwned,
    IReadOnlyList<UpgradeCreditSourceDto> Sources);

/// <summary>
///     Один grant, дающий credit на target-план.
/// </summary>
public sealed record UpgradeCreditSourceDto(
    Guid GrantId,
    Guid PlanId,
    string PlanDisplayName,
    string PlanTier,
    long CreditCents);
