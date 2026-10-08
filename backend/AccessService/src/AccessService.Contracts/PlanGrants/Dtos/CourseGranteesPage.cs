namespace AccessService.Contracts.PlanGrants.Dtos;

/// <summary>
///     Keyset-страница roster'а курса: пользователи с активным покрывающим grant'ом.
///     <see cref="NextCursor"/> = <c>null</c> когда страниц больше нет.
///     <see cref="TotalCount"/> — общее число держателей grant'а (для бейджа/счётчика).
///     Derive-модель "кто на курсе X" (epic access-derive-model, Phase 0).
/// </summary>
public sealed record CourseGranteesPage(
    IReadOnlyList<CourseGranteeDto> Items,
    string? NextCursor,
    int TotalCount);
