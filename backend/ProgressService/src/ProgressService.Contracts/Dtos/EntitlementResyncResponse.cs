namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Ответ resync-эндпоинтов entitlements: сколько тегов было добавлено / удалено в Redis
///     по сравнению с DB-источником истины. Если оба = 0 — drift'а не было, всё синхронно.
/// </summary>
public sealed record EntitlementResyncResponse(int TagsAdded, int TagsRemoved);
