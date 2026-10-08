using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.WebPush;

namespace NotificationService.Infrastructure.Postgres;

/// <summary>
/// Реализация доступа к web-push подпискам. Upsert/удаление — параметризованный raw SQL
/// (идемпотентность + race-safety через <c>ON CONFLICT</c>), чтения — через EF DbSet.
/// </summary>
public sealed class WebPushSubscriptionsRepository : IWebPushSubscriptionsRepository
{
    private readonly NotificationDbContext _dbContext;

    public WebPushSubscriptionsRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public Task UpsertAsync(WebPushSubscription subscription, CancellationToken cancellationToken = default) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notifications.web_push_subscriptions
                 (id, user_id, endpoint, p256dh, auth, user_agent, created_at, last_seen_at)
             VALUES (
                 {subscription.Id.Value}, {subscription.UserId}, {subscription.Endpoint},
                 {subscription.P256dh}, {subscription.Auth}, {subscription.UserAgent},
                 timezone('utc', now()), timezone('utc', now()))
             ON CONFLICT (endpoint) DO UPDATE SET
                 user_id = EXCLUDED.user_id,
                 p256dh = EXCLUDED.p256dh,
                 auth = EXCLUDED.auth,
                 user_agent = EXCLUDED.user_agent,
                 last_seen_at = timezone('utc', now())
             """,
            cancellationToken);

    public Task RemoveByEndpointAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM notifications.web_push_subscriptions
             WHERE user_id = {userId} AND endpoint = {endpoint}
             """,
            cancellationToken);

    public Task PruneEndpointAsync(string endpoint, CancellationToken cancellationToken = default) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             DELETE FROM notifications.web_push_subscriptions
             WHERE endpoint = {endpoint}
             """,
            cancellationToken);

    public async Task<IReadOnlyList<WebPushSubscription>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await _dbContext.WebPushSubscriptions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetUserIdsWithSubscriptionsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return new HashSet<Guid>();

        List<Guid> ids = await _dbContext.WebPushSubscriptions
            .AsNoTracking()
            .Where(x => userIds.Contains(x.UserId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return ids.ToHashSet();
    }
}
