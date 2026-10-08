using AccessService.Domain;

namespace AccessService.Core.Database;

public interface IOrderEventsRepository
{
    Task AddAsync(OrderEvent orderEvent, CancellationToken ct = default);

    Task<IReadOnlyList<OrderEvent>> GetByOrderIdAsync(Guid orderId, CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Guid orderId,
        OrderEventType eventType,
        CancellationToken ct = default);
}
