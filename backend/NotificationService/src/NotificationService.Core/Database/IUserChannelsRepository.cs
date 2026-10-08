using NotificationService.Domain.UserChannels;

namespace NotificationService.Core.Database;

/// <summary>
/// Доступ к пользовательским настройкам каналов / Access to per-user channel preferences.
/// </summary>
public interface IUserChannelsRepository
{
    /// <summary>
    /// Получить запись для пользователя. <c>null</c> — если записи нет (нужно использовать
    /// <see cref="UserNotificationChannels.Default"/>).
    /// </summary>
    Task<UserNotificationChannels?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Массовая выборка для dispatcher'а. Возвращает все существующие записи.
    /// Отсутствующие пользователи получат дефолты в самом dispatcher'е.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, UserNotificationChannels>> GetBulkAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарно создаёт строку с дефолтами (Email=ON, Telegram=OFF), если её ещё нет.
    /// Защита от race-condition при concurrent retry'ях <c>UserCreated</c>: PostgreSQL
    /// <c>ON CONFLICT (user_id) DO NOTHING</c>. Идемпотентно. Записывает напрямую через DbConnection
    /// + commit'ит сразу (не участвует в транзакции outbox'а).
    /// </summary>
    Task EnsureDefaultAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Атомарный upsert флагов / Atomic upsert of channel flags.
    /// <c>INSERT ... ON CONFLICT DO UPDATE</c> — одно SQL-обращение, без race.
    /// </summary>
    Task UpsertFlagsAsync(
        Guid userId,
        bool telegramEnabled,
        bool emailEnabled,
        bool webPushEnabled,
        CancellationToken cancellationToken = default);
}
