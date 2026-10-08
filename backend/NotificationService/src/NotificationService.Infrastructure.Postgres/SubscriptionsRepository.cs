using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.Subscriptions;

namespace NotificationService.Infrastructure.Postgres;

public sealed class SubscriptionsRepository : ISubscriptionsRepository, ISubscribersQuery
{
    private readonly NotificationDbContext _dbContext;

    public SubscriptionsRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public Task<bool> ExistsBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.Subscriptions.AnyAsync(predicate, ct);

    public Task<Subscription?> GetBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.Subscriptions
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlySet<Guid>> GetSubscribedUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        string entityType,
        Guid entityId,
        CancellationToken ct = default)
    {
        if (userIds.Count == 0)
            return new HashSet<Guid>();

        List<Guid> existing = await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.EntityType == entityType
                     && x.EntityId == entityId
                     && userIds.Contains(x.UserId))
            .Select(x => x.UserId)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

    public async Task AddAsync(Subscription subscription, CancellationToken ct = default)
    {
        await _dbContext.Subscriptions.AddAsync(subscription, ct);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<Subscription> subscriptions, CancellationToken ct = default)
    {
        if (subscriptions.Count == 0)
            return;

        await _dbContext.Subscriptions.AddRangeAsync(subscriptions, ct);
    }

    public async Task<IReadOnlyList<Subscription>> ListBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.Subscriptions
            .AsNoTracking()
            .Where(predicate)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);

    public async Task RemoveAsync(SubscriptionId id, CancellationToken ct = default)
    {
        await _dbContext.Subscriptions
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    // ISubscribersQuery
    public Task<int> CountByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default) =>
        _dbContext.Subscriptions.CountAsync(
            x => x.EntityType == entityType && x.EntityId == entityId,
            ct);

    public async Task<SubscriberPage> ByEntityPageAsync(
        string entityType,
        Guid entityId,
        Guid? afterUserId,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<Subscription> query = _dbContext.Subscriptions
            .AsNoTracking()
            .Where(x => x.EntityType == entityType && x.EntityId == entityId);

        if (afterUserId.HasValue)
        {
            Guid cursor = afterUserId.Value;
            query = query.Where(x => x.UserId > cursor);
        }

        List<Guid> userIds = await query
            .OrderBy(x => x.UserId)
            .Select(x => x.UserId)
            .Take(limit + 1)
            .ToListAsync(ct);

        bool hasMore = userIds.Count > limit;
        if (hasMore)
            userIds.RemoveAt(limit);

        Guid? nextAfterUserId = hasMore ? userIds[^1] : null;
        return new SubscriberPage(userIds, nextAfterUserId);
    }
}
