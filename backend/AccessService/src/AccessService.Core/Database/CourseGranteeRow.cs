using AccessService.Domain;

namespace AccessService.Core.Database;

/// <summary>
/// Read-model projection: один пользователь, чей активный grant покрывает курс.
/// Источник правды — <c>plan_grants × plans</c> (см.
/// <see cref="IPlanGrantsRepository.GetCourseGranteesKeysetAsync"/>). Используется
/// derive-моделью "кто на курсе X" (epic access-derive-model, Phase 0).
/// </summary>
/// <param name="UserId">Пользователь с покрывающим grant'ом.</param>
/// <param name="Source">Источник grant'а (<see cref="PlanGrantSource"/>).</param>
/// <param name="GrantedAt">Момент выдачи grant'а — ключ keyset-пагинации.</param>
/// <param name="PlanTier">Tier плана, через который выдан доступ.</param>
/// <param name="GrantId">Id grant'а — tiebreaker keyset-пагинации.</param>
public sealed record CourseGranteeRow(
    Guid UserId,
    PlanGrantSource Source,
    DateTimeOffset GrantedAt,
    PlanTier PlanTier,
    Guid GrantId);
