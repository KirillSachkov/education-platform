namespace AccessService.Contracts.HomePins;

/// <summary>
///     Строка списка закрепов плана для author-UI (<c>GET /access/plans/{id}/home-pins/</c>).
///     Title обогащается через ECS; при недоступности ECS soft-degrade'ит в пустую строку.
/// </summary>
public sealed record HomePinListItemDto(
    Guid PinId,
    Guid MaterialId,
    string Title,
    string? Note,
    string SortKey);

/// <summary>
///     Закреп для home-дашборда студента (<c>GET /access/me/home-pins/</c>). Обогащён
///     метаданными материала + lock-state. <see cref="Href"/> = <c>/knowledge-base/{materialId}</c>.
/// </summary>
public sealed record HomePinDto(
    Guid MaterialId,
    string Title,
    string Kind,
    string? ThumbnailUrl,
    string? Note,
    bool IsAccessible,
    string? LockReason,
    string Href);
