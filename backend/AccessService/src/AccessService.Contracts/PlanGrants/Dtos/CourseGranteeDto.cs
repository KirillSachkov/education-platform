namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Один пользователь, чей активный grant покрывает курс, + grant-метаданные.
///     Элемент <see cref="CourseGranteesPage"/> (derive-модель "кто на курсе X",
///     epic access-derive-model, Phase 0).
/// </summary>
/// <param name="UserId">Пользователь.</param>
/// <param name="Source">Источник grant'а (строка <c>PlanGrantSource</c>).</param>
/// <param name="GrantedAt">Момент выдачи grant'а.</param>
/// <param name="PlanTier">Tier плана, через который выдан доступ (строка <c>PlanTier</c>).</param>
/// <param name="GrantId">Id grant'а.</param>
public sealed record CourseGranteeDto(
    Guid UserId,
    string Source,
    DateTimeOffset GrantedAt,
    string PlanTier,
    Guid GrantId);
