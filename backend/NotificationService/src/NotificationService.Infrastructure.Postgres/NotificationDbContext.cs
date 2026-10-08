using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Domain.Subscriptions;
using NotificationService.Domain.UserChannels;
using NotificationService.Domain.UserOptOuts;
using NotificationService.Domain.WebPush;
using Wolverine.EntityFrameworkCore;

namespace NotificationService.Infrastructure.Postgres;

public class NotificationDbContext : DbContext
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<UserNotificationChannels> UserNotificationChannels => Set<UserNotificationChannels>();
    public DbSet<UserNotificationTypeOptOut> UserNotificationTypeOptOuts => Set<UserNotificationTypeOptOut>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();

    public NotificationDbContext(DbContextOptions<NotificationDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("notifications");
        modelBuilder.MapWolverineEnvelopeStorage("notifications");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
    }
}
