using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class OrdersRepository : IOrdersRepository
{
    private const long RenewalLockNamespace = 0x52454E4557414C00;

    private readonly AccessServiceDbContext _db;

    public OrdersRepository(AccessServiceDbContext db) => _db = db;

    public async Task<IAsyncDisposable?> TryAcquireRenewalLockAsync(
        Guid grantId,
        CancellationToken ct = default)
    {
        DbConnection connection = _db.Database.GetDbConnection();
        bool openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(ct);
        }

        long lockKey = GetRenewalLockKey(grantId);
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@lock_key)";
            DbParameter parameter = command.CreateParameter();
            parameter.ParameterName = "lock_key";
            parameter.DbType = DbType.Int64;
            parameter.Value = lockKey;
            command.Parameters.Add(parameter);

            object? result = await command.ExecuteScalarAsync(ct);
            if (result is not true)
            {
                if (openedHere)
                {
                    await connection.CloseAsync();
                }

                return null;
            }

            return new AdvisoryLockLease(connection, lockKey, openedHere);
        }
        catch
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }

            throw;
        }
    }

    public async Task AddAsync(Order order, CancellationToken ct = default) =>
        await _db.Orders.AddAsync(order, ct);

    public async Task<Result<Order, Error>> GetByAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default)
    {
        Order? order = await _db.Orders.FirstOrDefaultAsync(predicate, ct);
        return order is null
            ? Error.NotFound("order.not.found", "Заказ не найден")
            : order;
    }

    public async Task<IReadOnlyList<Order>> GetManyByAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.Orders.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.Orders.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<Order>> GetPendingForReconciliationAsync(
        string provider,
        DateTimeOffset minAgeCutoff,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default) =>
        await _db.Orders
            .Where(o => o.Status == OrderStatus.PENDING
                     && o.Provider == provider
                     && o.CreatedAt < minAgeCutoff)
            .Select(o => new
            {
                Order = o,
                LastDeferredAt = _db.OrderEvents
                    .Where(e => e.OrderId == o.Id
                        && e.EventType == OrderEventType.RECONCILIATION_DEFERRED)
                    .Max(e => (DateTimeOffset?)e.CreatedAt),
            })
            .Where(candidate => candidate.LastDeferredAt == null
                || candidate.LastDeferredAt <= retryCutoff)
            // Persistent round-robin: never-attempted rows first, then the row whose last
            // attempt is oldest. CreatedAt only breaks ties, so poison rows cannot cycle
            // back ahead of an untouched backlog after their cooldown expires.
            .OrderBy(candidate => candidate.LastDeferredAt != null)
            .ThenBy(candidate => candidate.LastDeferredAt)
            .ThenBy(candidate => candidate.Order.CreatedAt)
            .Take(limit)
            .Select(candidate => candidate.Order)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetPaidSubscriptionsWithoutRebillAsync(
        string provider,
        DateTimeOffset minAgeCutoff,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default) =>
        await (from order in _db.Orders
               join plan in _db.Plans on order.PlanId equals plan.Id
               where order.Status == OrderStatus.PAID
                  && order.ChargeType == OrderChargeType.INITIAL
                  && order.Provider == provider
                  && order.RebillId == null
                  && order.CreatedAt < minAgeCutoff
                  && plan.Tier == PlanTier.SUBSCRIPTION
               let lastDeferredAt = _db.OrderEvents
                   .Where(e => e.OrderId == order.Id
                       && e.EventType == OrderEventType.RECONCILIATION_DEFERRED)
                   .Max(e => (DateTimeOffset?)e.CreatedAt)
               where lastDeferredAt == null || lastDeferredAt <= retryCutoff
               orderby lastDeferredAt != null, lastDeferredAt, order.CreatedAt
               select order)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Order>> GetPaidRenewalsForRefundReconciliationAsync(
        string provider,
        DateTimeOffset createdAfter,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default) =>
        await _db.Orders
            .Where(o => o.Status == OrderStatus.PAID
                && o.ChargeType == OrderChargeType.RENEWAL
                && o.Provider == provider
                && o.CreatedAt >= createdAfter)
            .Select(o => new
            {
                Order = o,
                LastCheckedAt = _db.OrderEvents
                    .Where(e => e.OrderId == o.Id
                        && e.EventType == OrderEventType.REFUND_RECONCILIATION_CHECKED)
                    .Max(e => (DateTimeOffset?)e.CreatedAt),
            })
            .Where(candidate => candidate.LastCheckedAt == null
                || candidate.LastCheckedAt <= retryCutoff)
            .OrderBy(candidate => candidate.LastCheckedAt != null)
            .ThenBy(candidate => candidate.LastCheckedAt)
            .ThenBy(candidate => candidate.Order.CreatedAt)
            .Take(limit)
            .Select(candidate => candidate.Order)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<Order> Items, int Total)> SearchAsync(
        OrderStatus? status,
        Guid? userId,
        Guid? planId,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        string? correlationId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        IQueryable<Order> query = _db.Orders.AsNoTracking();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);
        if (userId.HasValue)
            query = query.Where(o => o.UserId == userId.Value);
        if (planId.HasValue)
            query = query.Where(o => o.PlanId == planId.Value);
        if (createdFrom.HasValue)
            query = query.Where(o => o.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue)
            query = query.Where(o => o.CreatedAt <= createdTo.Value);
        if (!string.IsNullOrWhiteSpace(correlationId))
            query = query.Where(o => o.CorrelationId == correlationId);

        int total = await query.CountAsync(ct);

        IReadOnlyList<Order> items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    private static long GetRenewalLockKey(Guid grantId)
    {
        Span<byte> bytes = stackalloc byte[16];
        grantId.TryWriteBytes(bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes[..8])
             ^ BinaryPrimitives.ReadInt64LittleEndian(bytes[8..])
             ^ RenewalLockNamespace;
    }

    private sealed class AdvisoryLockLease(
        DbConnection connection,
        long lockKey,
        bool closeConnection) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (connection.State == ConnectionState.Open)
                {
                    await using DbCommand command = connection.CreateCommand();
                    command.CommandText = "SELECT pg_advisory_unlock(@lock_key)";
                    DbParameter parameter = command.CreateParameter();
                    parameter.ParameterName = "lock_key";
                    parameter.DbType = DbType.Int64;
                    parameter.Value = lockKey;
                    command.Parameters.Add(parameter);
                    await command.ExecuteScalarAsync();
                }
            }
            finally
            {
                if (closeConnection && connection.State != ConnectionState.Closed)
                {
                    await connection.CloseAsync();
                }
            }
        }
    }
}
