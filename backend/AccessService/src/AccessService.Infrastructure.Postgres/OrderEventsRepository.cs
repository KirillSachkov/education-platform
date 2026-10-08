using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class OrderEventsRepository : IOrderEventsRepository
{
    private readonly AccessServiceDbContext _db;

    public OrderEventsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(OrderEvent orderEvent, CancellationToken ct = default) =>
        await _db.OrderEvents.AddAsync(orderEvent, ct);

    public async Task<IReadOnlyList<OrderEvent>> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default) =>
        await _db.OrderEvents
            .Where(e => e.OrderId == orderId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Guid orderId,
        OrderEventType eventType,
        CancellationToken ct = default) =>
        await _db.OrderEvents.AnyAsync(
            e => e.OrderId == orderId && e.EventType == eventType,
            ct);
}
