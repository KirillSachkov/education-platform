namespace ProgressService.Contracts.Requests;

/// <summary>
///     Батч-запрос для получения статуса «просмотрено» по списку материалов текущего пользователя.
///     Используется в лентах (space home, space materials, course materials) для отображения
///     галочки ✓ на уже просмотренных карточках без доп. запроса за каждым материалом.
/// </summary>
public sealed record GetMaterialViewStatusRequest(IReadOnlyCollection<Guid> MaterialIds);
