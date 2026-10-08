using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.UserOptOuts;

namespace NotificationService.Infrastructure.Postgres;

/// <summary>
/// EF + raw SQL реализация / EF + raw SQL implementation. <c>ReplaceAsync</c> делает
/// полный замен набора через атомарный upsert + delete-not-in-set внутри одной
/// транзакции — повторный вызов c тем же набором безопасен (idempotent), краш между
/// шагами не оставляет полу-удалённое состояние (см. issue #230, MSG-1).
/// </summary>
public sealed class UserOptOutsRepository : IUserOptOutsRepository
{
    private readonly NotificationDbContext _dbContext;

    public UserOptOutsRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public async Task<IReadOnlySet<NotificationType>> GetOptedOutTypesAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        List<NotificationType> rows = await _dbContext.UserNotificationTypeOptOuts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.Type)
            .ToListAsync(cancellationToken);

        return rows.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlySet<NotificationType>>> GetOptedOutBulkAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new Dictionary<Guid, IReadOnlySet<NotificationType>>();

        List<UserNotificationTypeOptOut> rows = await _dbContext.UserNotificationTypeOptOuts
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .ToListAsync(cancellationToken);

        Dictionary<Guid, HashSet<NotificationType>> grouped = [];
        foreach (UserNotificationTypeOptOut row in rows)
        {
            if (!grouped.TryGetValue(row.UserId, out HashSet<NotificationType>? set))
            {
                set = [];
                grouped[row.UserId] = set;
            }
            set.Add(row.Type);
        }

        Dictionary<Guid, IReadOnlySet<NotificationType>> result = new(grouped.Count);
        foreach (KeyValuePair<Guid, HashSet<NotificationType>> kvp in grouped)
            result[kvp.Key] = kvp.Value;
        return result;
    }

    public async Task ReplaceAsync(
        Guid userId,
        IReadOnlyCollection<NotificationType> optedOutTypes,
        CancellationToken cancellationToken = default)
    {
        // Атомарный full-replace через явную транзакцию. Краш между upsert и delete
        // оставит предыдущее состояние нетронутым (либо обе операции применились,
        // либо ни одна). Raw SQL обходит ChangeTracker — `SaveChangesAsync` тут
        // не нужен; внешнюю транзакцию `ITransactionManager` НЕ открываем, потому
        // что caller (`UpdateMyPreferencesHandler`) не публикует integration events.
        short[] typesArr = [..
            new HashSet<NotificationType>(optedOutTypes).Select(t => (short)t)];

        await using IDbContextTransaction tx =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        if (typesArr.Length == 0)
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 DELETE FROM notifications.user_notification_type_optouts
                 WHERE user_id = {userId}
                 """,
                cancellationToken);
        }
        else
        {
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO notifications.user_notification_type_optouts (user_id, type, opted_out_at)
                 SELECT {userId}::uuid, t, timezone('utc', now())
                 FROM unnest({typesArr}::smallint[]) AS t
                 ON CONFLICT (user_id, type) DO NOTHING
                 """,
                cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 DELETE FROM notifications.user_notification_type_optouts
                 WHERE user_id = {userId} AND type <> ALL({typesArr}::smallint[])
                 """,
                cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }
}
