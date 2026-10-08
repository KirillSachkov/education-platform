using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;

namespace NotificationService.Infrastructure.Postgres;

public sealed class DeliveriesRepository : IDeliveriesRepository
{
    private readonly NotificationDbContext _dbContext;

    public DeliveriesRepository(NotificationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(NotificationDelivery delivery, CancellationToken cancellationToken = default)
    {
        await _dbContext.NotificationDeliveries.AddAsync(delivery, cancellationToken);
    }

    public Task<NotificationDelivery?> GetForNotificationAsync(
        NotificationId notificationId,
        NotificationChannel channel,
        CancellationToken cancellationToken = default) =>
        _dbContext.NotificationDeliveries
            .FirstOrDefaultAsync(
                d => d.NotificationId == notificationId && d.Channel == channel,
                cancellationToken);
}
