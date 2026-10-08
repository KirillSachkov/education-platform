namespace ProgressService.Contracts.Requests;

/// <summary>
///     Staff-override: отметить материал изученным ЗА студента (<paramref name="UserId"/>).
///     Вызывается админом / модератором / автором-владельцем курса. В отличие от self-эндпоинта
///     (<c>POST /progress/materials/{id}/view</c>) цель — указанный <paramref name="UserId"/>,
///     а не текущий пользователь. Entitlement проверяется против ЦЕЛЕВОГО юзера (issue #398).
/// </summary>
public sealed record MarkMaterialViewedForUserRequest(Guid UserId);
