using NotificationService.Domain.Notifications;

namespace NotificationService.Core.Database;

/// <summary>
/// Доступ к per-user per-type отпискам / Per-user per-type opt-outs access.
/// Отсутствие строки = пользователь подписан на этот тип (дефолт). Присутствие = отписан.
/// </summary>
public interface IUserOptOutsRepository
{
    /// <summary>
    /// Вернуть типы, от которых пользователь отписался. Пустой set — подписан на всё.
    /// </summary>
    Task<IReadOnlySet<NotificationType>> GetOptedOutTypesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовая выборка для dispatcher'а. Ключи — userId, значения — set типов (может быть пустой).
    /// Пользователи без ни одной opt-out-строки в словарь НЕ попадают — dispatcher должен интерпретировать
    /// отсутствие ключа как «подписан на всё».
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlySet<NotificationType>>> GetOptedOutBulkAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Заменить полный набор отписок для пользователя / Replace the full opt-out set for a user.
    /// Атомарно: удаляет всё старое + вставляет новое. Идемпотентно при совпадающих наборах.
    /// </summary>
    Task ReplaceAsync(
        Guid userId,
        IReadOnlyCollection<NotificationType> optedOutTypes,
        CancellationToken cancellationToken = default);
}
