namespace AccessService.Contracts.PlanGrants.Requests;

/// <summary>
/// Request payload for <c>POST /access/admin/users/{userId}/trial-credit-override/</c> (#580).
/// Продлевает действие зачёта (trial-кредита) для пробного гранта пользователя.
/// </summary>
/// <param name="PlanId">ID пробного плана, на котором у пользователя есть grant.</param>
/// <param name="Until">
/// До какого момента действует оверрайд зачёта. <c>null</c> = по умолчанию now + 30 дней.
/// </param>
public sealed record TrialCreditOverrideRequest(
    Guid PlanId,
    DateTimeOffset? Until);
