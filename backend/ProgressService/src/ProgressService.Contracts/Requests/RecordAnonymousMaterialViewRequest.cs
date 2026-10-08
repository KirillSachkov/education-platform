namespace ProgressService.Contracts.Requests;

/// <summary>
///     Запись анонимного просмотра материала. <paramref name="AnonymousId"/> — стабильный
///     UUID v4 из cookie <c>plu_anon_id</c> на фронте. Идемпотентно: повторный POST с
///     тем же anonymousId + materialId возвращает 200 OK без вставки.
/// </summary>
public sealed record RecordAnonymousMaterialViewRequest(string AnonymousId);
