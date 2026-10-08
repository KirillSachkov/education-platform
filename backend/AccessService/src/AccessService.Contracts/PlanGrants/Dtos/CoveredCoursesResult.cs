namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Набор courseId'ов, покрытых активными grant'ами пользователя (explicit COURSE ∪
///     вся платформа для FULL_ALL/LEARN_ALL ∪ legacy FREE-авторы). Derive-модель
///     "мои курсы" (epic access-derive-model, Phase 0). Может быть отфильтрован по
///     конкретному автору (см. <see cref="Requests.CoveredCoursesRequest"/>).
/// </summary>
public sealed record CoveredCoursesResult(
    IReadOnlyList<Guid> CourseIds);
