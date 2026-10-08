namespace AccessService.Core.Database;

/// <summary>
/// Read-model projection: scope покрытия активных grant'ов одного пользователя,
/// разложенный по способу покрытия. Используется derive-моделью "мои курсы"
/// (epic access-derive-model, Phase 0): AccessService раскрывает global
/// FULL_ALL / LEARN_ALL через ECS <c>GetAllCourseIdsAsync</c>, legacy FREE —
/// через <c>GetAuthorCourseIdsAsync</c>, и объединяет всё с
/// <see cref="ExplicitCourseIds"/>.
/// </summary>
/// <param name="ExplicitCourseIds">
/// Курсы, покрытые COURSE-grant'ами напрямую (<c>plan.course_id</c>).
/// </param>
/// <param name="HasGlobalCourseAccess">
/// Есть ли активный FULL_ALL / LEARN_ALL grant. Такой grant покрывает всю платформу,
/// независимо от автора плана.
/// </param>
/// <param name="FreeAuthorIds">
/// Авторы, покрытые legacy FREE-grant'ом. FREE deprecated (#358); поле оставлено
/// для архивных строк и lower-priority обработки (см. doc §8 Q1).
/// </param>
public sealed record UserGrantScope(
    IReadOnlyList<Guid> ExplicitCourseIds,
    bool HasGlobalCourseAccess,
    IReadOnlyList<Guid> FreeAuthorIds);
