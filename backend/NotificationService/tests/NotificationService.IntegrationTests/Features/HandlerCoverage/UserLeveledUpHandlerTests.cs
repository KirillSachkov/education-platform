using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// L1 handler coverage для <c>user.leveled_up</c> (#555): повышение gamification-уровня →
/// самому пользователю приходит одно поздравительное уведомление (InApp + Telegram). Событие
/// самодостаточно (номер уровня + XP в payload), внешних lookup'ов нет. Идемпотентность по
/// детерминированному correlation (userId × newLevel) — повторная доставка не дублирует.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class UserLeveledUpHandlerTests : NotificationServiceTestsBase
{
    public UserLeveledUpHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task LeveledUp_CreatesCongratulationForUser()
    {
        Guid userId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new UserLeveledUp(
            UserId: userId,
            PreviousLevel: 4,
            NewLevel: 5,
            TotalXp: 980));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == userId));

        Assert.Equal(NotificationType.UserLeveledUp, notification.Type);
        Assert.Contains("5", notification.Title, StringComparison.Ordinal);
        Assert.Contains("980", notification.Body, StringComparison.Ordinal);
        // Dispatcher запекает targetUrl через PlatformLinkBuilder → /home (XP-карточка + продолжить курс).
        Assert.Contains("/home", notification.Payload, StringComparison.Ordinal);
        // payload несёт newLevel/totalXp для фронтовой модалки с конфетти.
        Assert.Contains("newLevel", notification.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeveledUp_Twice_SameLevel_IsIdempotent()
    {
        Guid userId = Guid.NewGuid();

        UserLeveledUp evt = new(UserId: userId, PreviousLevel: 4, NewLevel: 5, TotalXp: 980);

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId));

        Assert.Equal(1, count);
    }
}
