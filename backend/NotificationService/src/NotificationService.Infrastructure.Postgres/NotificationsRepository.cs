using System.Linq.Expressions;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using SharedKernel;

namespace NotificationService.Infrastructure.Postgres;

public sealed class NotificationsRepository : INotificationsRepository
{
    private readonly NotificationDbContext _dbContext;

    public NotificationsRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<Notification, Error>> GetBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Notification? notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(predicate, cancellationToken);

        if (notification is null)
            return Error.NotFound("notification.not.found", "Уведомление не найдено");

        return notification;
    }

    public async Task<IReadOnlyList<Notification>> ListForUserAsync(
        Guid userId,
        int limit,
        DateTime? cursorBefore,
        Guid? cursorId,
        bool unreadOnly,
        IReadOnlyList<short>? types,
        CancellationToken cancellationToken = default)
    {
        // Keyset pagination через raw SQL: EF не может транслировать tiebreaker
        // `x.Id < cursorId`, потому что `Notification.Id` — value-object (`NotificationId`)
        // с `HasConversion`, и <-оператор на VO не транслируется в SQL.
        // FromSqlInterpolated материализует через стандартный EF-mapper (работает с private ctor
        // через shadow properties) и позволяет составить полноценный compound-cursor в WHERE.
        //
        // Batch-insert в dispatcher'е даёт одинаковый `CreatedAt` для всех notification'ов
        // в пределах одного fan-out'а (UUID v7 выдаёт monotonic `Id`, но `CreatedAt = DateTime.UtcNow`
        // без counter'а — разрешение зависит от Stopwatch.Frequency). Tiebreaker по id закрывает
        // этот edge case: в пределах тех же Ticks ordering по Id стабилен.
        bool applyCursor = cursorBefore.HasValue && cursorId.HasValue;
        DateTime cursorTs = cursorBefore ?? DateTime.MinValue;
        Guid cursorGuidValue = cursorId ?? Guid.Empty;

        // Передаём types как short[]; пустой массив = «фильтр выключен» — благодаря
        // условию `cardinality({{typesArr}}) = 0 OR …` SQL-планировщик пропускает фильтр.
        bool hasTypeFilter = types is { Count: > 0 };
        short[] typesArr = hasTypeFilter ? types!.ToArray() : Array.Empty<short>();

        // Все литералы параметризованы через FormattableString — EF эмитит @p0, @p1, ...
        // в готовый query, SQL-injection невозможен. Колонки перечислены явно: SELECT *
        // расширяется молча при добавлении новых полей и хуже кэшируется план-кэшем Npgsql
        // (issue #230, DB-1). Порядок совпадает с EF-mapper'ом для FromSqlInterpolated.
        FormattableString sql = $$"""
            SELECT id, recipient_user_id, type, template_id, title, body,
                   channels, payload, correlation_id, created_at, read_at
            FROM notifications.notifications
            WHERE recipient_user_id = {{userId}}
              AND ({{unreadOnly}} = false OR read_at IS NULL)
              AND (cardinality({{typesArr}}::smallint[]) = 0 OR type = ANY({{typesArr}}::smallint[]))
              AND ({{applyCursor}} = false
                   OR (created_at < {{cursorTs}})
                   OR (created_at = {{cursorTs}} AND id < {{cursorGuidValue}}))
            ORDER BY created_at DESC, id DESC
            LIMIT {{limit}}
            """;

        List<Notification> result = await _dbContext.Notifications
            .FromSqlInterpolated(sql)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return result;
    }

    public Task<int> CountBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _dbContext.Notifications.CountAsync(predicate, cancellationToken);

    public Task<bool> ExistsBy(
        Expression<Func<Notification, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _dbContext.Notifications.AnyAsync(predicate, cancellationToken);

    public async Task<bool> HasUnreadOfTypeForSubmissionAsync(
        Guid recipientUserId,
        NotificationType type,
        Guid submissionId,
        CancellationToken cancellationToken = default)
    {
        // Bounded existence-запрос по jsonb payload: submissionId лежит в payload как
        // строковый guid (System.Text.Json пишет lowercase "D", как и Guid.ToString()).
        // Индекс ix_notifications_recipient_unread (recipient WHERE read_at IS NULL) покрывает
        // хвост фильтра; type + payload-match проверяются на немногих непрочитанных строках.
        short typeCode = (short)type;
        string submissionIdStr = submissionId.ToString();

        List<bool> rows = await _dbContext.Database
            .SqlQuery<bool>($"""
                SELECT EXISTS (
                    SELECT 1
                    FROM notifications.notifications
                    WHERE recipient_user_id = {recipientUserId}
                      AND type = {typeCode}
                      AND read_at IS NULL
                      AND payload->>'submissionId' = {submissionIdStr}
                ) AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return rows.Count > 0 && rows[0];
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        return await _dbContext.Notifications
            .Where(x => x.RecipientUserId == userId && x.ReadAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.ReadAt, _ => now),
                cancellationToken);
    }
}
