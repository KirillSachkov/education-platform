using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotificationService.Core.Database;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.IntegrationTests.Features.Preferences;

/// <summary>
///     Per-type opt-out (B3) — проверяем, что диспатчер НЕ создаёт <c>Notification</c>, если
///     у пользователя есть запись в <c>user_notification_type_optouts</c> на соответствующий тип.
///     Используем <c>plan_grant.created</c> (живой <c>PlanGrantReceivedHandler</c>) как vehicle —
///     <c>CourseEnrolled</c> снят в access-derive-model Phase 4.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class OptOutDispatchTests : NotificationServiceTestsBase
{
    public OptOutDispatchTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PlanGrantReceived_UserOptedOut_ShouldNotCreateNotification()
    {
        Guid userId = Guid.NewGuid();

        // Предварительно записываем opt-out: юзер отказался от PlanGrantReceived.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IUserOptOutsRepository repo = scope.ServiceProvider.GetRequiredService<IUserOptOutsRepository>();
            await repo.ReplaceAsync(userId, [NotificationType.PlanGrantReceived]);
        }

        await InvokeMessageAndWaitAsync(BuildEvent(userId));

        int notificationCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId && x.Type == NotificationType.PlanGrantReceived));

        Assert.Equal(0, notificationCount);
    }

    [Fact]
    public async Task PlanGrantReceived_UserOptedOutOfDifferentType_ShouldStillCreateNotification()
    {
        Guid userId = Guid.NewGuid();

        // Opt-out на другой тип — не должен мешать PlanGrantReceived-нотификации.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IUserOptOutsRepository repo = scope.ServiceProvider.GetRequiredService<IUserOptOutsRepository>();
            await repo.ReplaceAsync(userId, [NotificationType.MaterialPublished]);
        }

        await InvokeMessageAndWaitAsync(BuildEvent(userId));

        int notificationCount = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == userId && x.Type == NotificationType.PlanGrantReceived));

        Assert.Equal(1, notificationCount);
    }

    [Fact]
    public async Task Replace_RemovesPreviousOptOuts()
    {
        Guid userId = Guid.NewGuid();

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        IUserOptOutsRepository repo = scope.ServiceProvider.GetRequiredService<IUserOptOutsRepository>();

        await repo.ReplaceAsync(userId, [NotificationType.PlanGrantReceived, NotificationType.MaterialPublished]);
        Assert.Equal(2, (await repo.GetOptedOutTypesAsync(userId)).Count);

        // Replace с пустым набором = полный unsubscribe-from-optouts.
        await repo.ReplaceAsync(userId, Array.Empty<NotificationType>());
        IReadOnlySet<NotificationType> after = await repo.GetOptedOutTypesAsync(userId);
        Assert.Empty(after);
    }

    private static PlanGrantCreated BuildEvent(Guid userId) =>
        new(
            GrantId: Guid.NewGuid(),
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: "FULL_ALL",
            PlanAuthorId: Guid.NewGuid(),
            CourseId: null,
            IncludesFutureContent: true,
            Source: "ADMIN_GRANT",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null);
}
