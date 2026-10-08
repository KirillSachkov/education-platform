using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.UserChannels;

namespace NotificationService.Infrastructure.Postgres;

public sealed class UserChannelsRepository : IUserChannelsRepository
{
    private readonly NotificationDbContext _dbContext;

    public UserChannelsRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public Task<UserNotificationChannels?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        _dbContext.UserNotificationChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, UserNotificationChannels>> GetBulkAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, UserNotificationChannels>();

        List<UserNotificationChannels> rows = await _dbContext.UserNotificationChannels
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, UserNotificationChannels> map = new(rows.Count);
        foreach (UserNotificationChannels row in rows)
            map[row.UserId] = row;
        return map;
    }

    /// <summary>
    /// Атомарный insert-if-missing через <c>ON CONFLICT DO NOTHING</c> — защита от race
    /// при concurrent <c>UserCreated</c>. Параметризован — SQL injection невозможен.
    /// </summary>
    public Task EnsureDefaultAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notifications.user_notification_channels
                 (user_id, telegram_enabled, email_enabled, web_push_enabled, updated_at)
             VALUES ({userId}, true, true, true, timezone('utc', now()))
             ON CONFLICT (user_id) DO NOTHING
             """,
            cancellationToken);

    public Task UpsertFlagsAsync(
        Guid userId,
        bool telegramEnabled,
        bool emailEnabled,
        bool webPushEnabled,
        CancellationToken cancellationToken = default) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notifications.user_notification_channels
                 (user_id, telegram_enabled, email_enabled, web_push_enabled, updated_at)
             VALUES ({userId}, {telegramEnabled}, {emailEnabled}, {webPushEnabled}, timezone('utc', now()))
             ON CONFLICT (user_id) DO UPDATE SET
                 telegram_enabled = EXCLUDED.telegram_enabled,
                 email_enabled = EXCLUDED.email_enabled,
                 web_push_enabled = EXCLUDED.web_push_enabled,
                 updated_at = EXCLUDED.updated_at
             """,
            cancellationToken);
}
