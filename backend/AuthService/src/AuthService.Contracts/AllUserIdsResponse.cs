namespace AuthService.Contracts;

/// <summary>
///     Страница keyset-пагинации по всем активным пользователям (#532).
///     <see cref="NextAfterId"/> = null — страниц больше нет.
/// </summary>
public sealed record AllUserIdsResponse(
    IReadOnlyList<Guid> UserIds,
    Guid? NextAfterId);
