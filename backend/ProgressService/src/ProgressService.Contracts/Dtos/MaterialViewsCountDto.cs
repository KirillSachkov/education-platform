namespace ProgressService.Contracts.Dtos;

/// <summary>
///     Суммарный счётчик уникальных просмотров материала: уникальные авторизованные
///     пользователи (<c>material_views</c>) плюс уникальные анонимы по cookie
///     (<c>anonymous_material_views</c>).
/// </summary>
public sealed record MaterialViewsCountDto(Guid MaterialId, long Count);
