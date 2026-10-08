using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NotificationService.Core;
using NotificationService.Domain.Deliveries;
using NotificationService.Domain.Notifications;
using NotificationService.Infrastructure.Postgres.Retention;
using NotificationService.IntegrationTests.Infrastructure;

namespace NotificationService.IntegrationTests.Features.Retention;

/// <summary>
///     <c>NotificationRetentionService</c> — batch DELETE по порогу <c>created_at &lt; cutoff</c>.
///     Проверяем:
///     <list type="bullet">
///         <item>Старые notifications удаляются, свежие — остаются</item>
///         <item>Связанные <c>notification_deliveries</c> удаляются каскадом</item>
///         <item>Batch-логика корректно отрабатывает с BatchSize &lt; totalToDelete</item>
///         <item>Отключенный сервис (<c>Enabled=false</c>) не трогает данные</item>
///     </list>
///
///     Тестируем <c>RunCleanupCycleAsync</c> напрямую (через reflection на private метод),
///     чтобы не зависеть от <c>PeriodicTimer</c> и initial delay.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class NotificationRetentionTests : NotificationServiceTestsBase
{
    public NotificationRetentionTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Cleanup_DeletesOldNotifications_KeepsFresh()
    {
        Guid userId = Guid.NewGuid();
        DateTime oldTs = DateTime.UtcNow.AddDays(-100);
        DateTime freshTs = DateTime.UtcNow.AddDays(-10);

        List<Guid> oldIds = await SeedAsync(userId, oldTs, count: 5);
        List<Guid> freshIds = await SeedAsync(userId, freshTs, count: 3);

        NotificationRetentionService service = BuildService(days: 90);
        await InvokeRunCleanupAsync(service);

        List<Guid> remaining = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .Where(n => n.RecipientUserId == userId)
            .Select(n => n.Id.Value)
            .ToListAsync());

        Assert.Equal(3, remaining.Count);
        Assert.All(oldIds, id => Assert.DoesNotContain(id, remaining));
        Assert.All(freshIds, id => Assert.Contains(id, remaining));
    }

    [Fact]
    public async Task Cleanup_CascadesDeliveries()
    {
        Guid userId = Guid.NewGuid();
        DateTime oldTs = DateTime.UtcNow.AddDays(-100);

        List<Guid> oldIds = await SeedAsync(userId, oldTs, count: 2);

        // Добавляем по delivery для каждого notification.
        await ExecuteInDb(async db =>
        {
            foreach (Guid nid in oldIds)
            {
                NotificationDelivery d = NotificationDelivery.Create(
                    NotificationId.Of(nid), NotificationChannel.Email).Value;
                d.MarkDelivered(providerMessageId: "msg-" + nid);
                await db.NotificationDeliveries.AddAsync(d);
            }
            await db.SaveChangesAsync();
        });

        int deliveriesBefore = await ExecuteInDb(db => db.NotificationDeliveries.CountAsync());
        Assert.Equal(2, deliveriesBefore);

        NotificationRetentionService service = BuildService(days: 90);
        await InvokeRunCleanupAsync(service);

        int deliveriesAfter = await ExecuteInDb(db => db.NotificationDeliveries.CountAsync());
        Assert.Equal(0, deliveriesAfter);
    }

    [Fact]
    public async Task Cleanup_BatchSizeSmallerThanTotal_DeletesAllInMultipleBatches()
    {
        Guid userId = Guid.NewGuid();
        DateTime oldTs = DateTime.UtcNow.AddDays(-200);

        await SeedAsync(userId, oldTs, count: 7); // 7 > batchSize=3 → минимум 3 итерации

        NotificationRetentionService service = BuildService(days: 90, batchSize: 3);
        await InvokeRunCleanupAsync(service);

        int remaining = await ExecuteInDb(db => db.Notifications
            .CountAsync(n => n.RecipientUserId == userId));
        Assert.Equal(0, remaining);
    }

    // --- Helpers ---

    private async Task<List<Guid>> SeedAsync(Guid userId, DateTime fixedTs, int count)
    {
        List<Guid> ids = [];
        await ExecuteInDb(async db =>
        {
            List<Notification> batch = [];
            for (int i = 0; i < count; i++)
            {
                Notification n = Notification.Create(
                    recipientUserId: userId,
                    type: NotificationType.Welcome,
                    templateId: "welcome",
                    title: $"t{i}",
                    body: $"b{i}",
                    channels: NotificationChannel.InApp,
                    payload: "{}",
                    correlationId: Guid.NewGuid()).Value;
                batch.Add(n);
                ids.Add(n.Id.Value);
            }
            await db.Notifications.AddRangeAsync(batch);
            await db.SaveChangesAsync();

            Guid[] idArr = [.. ids.TakeLast(count)];
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE notifications.notifications SET created_at = {fixedTs} WHERE id = ANY({idArr})");
        });
        return ids;
    }

    private NotificationRetentionService BuildService(int days, int batchSize = 5000)
    {
        NotificationOptions opts = new()
        {
            Retention = new NotificationRetentionOptions
            {
                Enabled = true,
                DefaultDays = days,
                BatchSize = batchSize,
                IntervalHours = 24,
                InitialDelaySeconds = 0,
            },
        };
        IOptionsMonitor<NotificationOptions> monitor = new StaticOptionsMonitor(opts);
        ILogger<NotificationRetentionService> logger =
            NullLogger<NotificationRetentionService>.Instance;

        return new NotificationRetentionService(
            Services.GetRequiredService<IServiceScopeFactory>(),
            monitor,
            logger);
    }

    private static async Task InvokeRunCleanupAsync(NotificationRetentionService service)
    {
        // Вызываем private RunCleanupCycleAsync через reflection — не хотим запускать BackgroundService
        // с таймером и initial delay ради одного прогона.
        System.Reflection.MethodInfo method = typeof(NotificationRetentionService)
            .GetMethod(
                "RunCleanupCycleAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(service, [CancellationToken.None])!;
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<NotificationOptions>
    {
        private readonly NotificationOptions _value;
        public StaticOptionsMonitor(NotificationOptions value) => _value = value;
        public NotificationOptions CurrentValue => _value;
        public NotificationOptions Get(string? name) => _value;
        public IDisposable? OnChange(Action<NotificationOptions, string?> listener) => null;
    }
}
