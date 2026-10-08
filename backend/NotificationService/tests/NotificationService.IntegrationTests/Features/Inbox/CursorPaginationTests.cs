using Microsoft.EntityFrameworkCore;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace NotificationService.IntegrationTests.Features.Inbox;

/// <summary>
/// Регрессионные тесты на cursor pagination: raw-SQL keyset с compound-cursor
/// (created_at DESC, id DESC). Проверяет edge case timestamp collision — когда batch-insert
/// в fan-out'е даёт одинаковый CreatedAt для нескольких записей.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CursorPaginationTests : NotificationServiceTestsBase
{
    public CursorPaginationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Pagination_HandlesTimestampCollision_WithoutDuplicatesOrGaps()
    {
        Guid userId = Guid.NewGuid();
        DateTime sharedTs = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

        // Seed 25 notifications — все с одинаковым CreatedAt, но разные Id (UUID v7 monotonic).
        // Пагинация лимитом 10 должна вернуть все 25 за 3 запроса без дубликатов и пропусков.
        List<Guid> createdIds = await SeedWithFixedTimestampAsync(userId, sharedTs, count: 25);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        INotificationsRepository repo = scope.ServiceProvider.GetRequiredService<INotificationsRepository>();

        List<Guid> collected = [];

        // Page 1
        IReadOnlyList<Notification> page1 = await repo.ListForUserAsync(
            userId, limit: 10,
            cursorBefore: null, cursorId: null,
            unreadOnly: false, types: null, cancellationToken: default);
        Assert.Equal(10, page1.Count);
        foreach (Notification n in page1)
            collected.Add(n.Id.Value);

        // Page 2 — cursor = last of page 1
        Notification lastP1 = page1[^1];
        IReadOnlyList<Notification> page2 = await repo.ListForUserAsync(
            userId, limit: 10,
            cursorBefore: lastP1.CreatedAt, cursorId: lastP1.Id.Value,
            unreadOnly: false, types: null, cancellationToken: default);
        Assert.Equal(10, page2.Count);
        foreach (Notification n in page2)
            collected.Add(n.Id.Value);

        // Page 3 — cursor = last of page 2
        Notification lastP2 = page2[^1];
        IReadOnlyList<Notification> page3 = await repo.ListForUserAsync(
            userId, limit: 10,
            cursorBefore: lastP2.CreatedAt, cursorId: lastP2.Id.Value,
            unreadOnly: false, types: null, cancellationToken: default);
        Assert.Equal(5, page3.Count);
        foreach (Notification n in page3)
            collected.Add(n.Id.Value);

        // Все 25 id должны быть собраны, уникальны.
        Assert.Equal(25, collected.Count);
        Assert.Equal(25, collected.Distinct().Count());
        Assert.All(createdIds, id => Assert.Contains(id, collected));
    }

    [Fact]
    public async Task Pagination_MixedTimestamps_OrdersDescByCreatedAtThenId()
    {
        // Смесь: 10 старых (t1), 10 новых (t2), + 5 с t2-collision. Pagination должен вернуть
        // их в порядке createdAt DESC, и в пределах одинакового createdAt — по id DESC.
        Guid userId = Guid.NewGuid();
        DateTime t1 = new(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc);
        DateTime t2 = new(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);

        await SeedWithFixedTimestampAsync(userId, t1, 10);
        await SeedWithFixedTimestampAsync(userId, t2, 15);

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        INotificationsRepository repo = scope.ServiceProvider.GetRequiredService<INotificationsRepository>();

        // Забираем всё пачкой по 8 — 4 страницы (8+8+8+1). t2 должен идти первым, потом t1.
        List<Notification> all = [];
        DateTime? cursor = null;
        Guid? cursorId = null;
        while (true)
        {
            IReadOnlyList<Notification> page = await repo.ListForUserAsync(
                userId, limit: 8, cursorBefore: cursor, cursorId: cursorId,
                unreadOnly: false, types: null, cancellationToken: default);
            if (page.Count == 0) break;
            all.AddRange(page);
            if (page.Count < 8) break;
            cursor = page[^1].CreatedAt;
            cursorId = page[^1].Id.Value;
        }

        Assert.Equal(25, all.Count);
        Assert.Equal(25, all.Select(x => x.Id.Value).Distinct().Count());

        // Первые 15 — все должны иметь t2 (они моложе), потом 10 с t1.
        Assert.All(all.Take(15), n => Assert.Equal(t2, n.CreatedAt));
        Assert.All(all.Skip(15), n => Assert.Equal(t1, n.CreatedAt));
    }

    [Fact]
    public async Task Pagination_UnreadOnly_FiltersReadAtNotNull()
    {
        Guid userId = Guid.NewGuid();
        DateTime ts = new(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc);

        await SeedWithFixedTimestampAsync(userId, ts, 10);

        // Помечаем первые 6 как read.
        await ExecuteInDb(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE notifications.notifications
             SET read_at = {ts}
             WHERE recipient_user_id = {userId}
               AND id IN (SELECT id FROM notifications.notifications
                          WHERE recipient_user_id = {userId}
                          ORDER BY id LIMIT 6)
             """));

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        INotificationsRepository repo = scope.ServiceProvider.GetRequiredService<INotificationsRepository>();

        IReadOnlyList<Notification> unread = await repo.ListForUserAsync(
            userId, limit: 20, cursorBefore: null, cursorId: null,
            unreadOnly: true, types: null, cancellationToken: default);

        Assert.Equal(4, unread.Count);
        Assert.All(unread, n => Assert.Null(n.ReadAt));
    }

    private async Task<List<Guid>> SeedWithFixedTimestampAsync(Guid userId, DateTime sharedTs, int count)
    {
        List<Guid> ids = [];
        await ExecuteInDb(async db =>
        {
            List<Notification> toInsert = [];
            for (int i = 0; i < count; i++)
            {
                Notification n = Notification.Create(
                    recipientUserId: userId,
                    type: NotificationType.Welcome,
                    templateId: "welcome",
                    title: $"Title {i}",
                    body: $"Body {i}",
                    channels: NotificationChannel.InApp,
                    payload: "{}",
                    correlationId: Guid.NewGuid()).Value;
                toInsert.Add(n);
                ids.Add(n.Id.Value);
            }

            await db.Notifications.AddRangeAsync(toInsert);
            await db.SaveChangesAsync();

            // Затираем CreatedAt одинаковым timestamp — симулируем batch-insert collision.
            // EF HasConversion не даёт поменять через LINQ update (`x.Id.Value` не транслируется),
            // используем raw SQL. ВАЖНО: фильтр по только что вставленным id, а не по userId —
            // иначе затираем previous batch'и в mixed-timestamp сценариях.
            Guid[] idArr = [.. ids.TakeLast(count)];
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE notifications.notifications SET created_at = {sharedTs} WHERE id = ANY({idArr})");
        });

        return ids;
    }
}
