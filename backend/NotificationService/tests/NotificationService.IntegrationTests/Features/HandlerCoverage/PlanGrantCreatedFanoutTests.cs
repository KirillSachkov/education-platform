using AuthService.Contracts;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using NotificationService.Domain.Notifications;
using NotificationService.IntegrationTests.Infrastructure;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace NotificationService.IntegrationTests.Features.HandlerCoverage;

/// <summary>
/// Regression-lock для прод-инцидента #444: одна <c>plan_grant.created</c> по платной покупке
/// ДОЛЖНА родить ОБА уведомления одновременно — buyer (<see cref="NotificationType.PlanGrantReceived"/>)
/// и author (<see cref="NotificationType.PlanGrantAuthorSale"/>). На проде пустой
/// <c>AccessServiceOptions.Url</c> валил конструктор <c>SubscribeOnPlanGrantCreatedHandler</c>
/// (sibling на той же очереди <c>notifications.access.grant_events</c>) ещё до тела хендлеров →
/// весь envelope уходил в dead-letter → НИ покупатель, НИ автор не получали ничего.
/// Per-handler тесты этого co-existence не проверяли (каждый гонял свой хендлер изолированно).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanGrantCreatedFanoutTests : NotificationServiceTestsBase
{
    public PlanGrantCreatedFanoutTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PlanGrantCreated_Purchase_NotifiesBothBuyerAndAuthor()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Иван Петров", username: "ivan", email: "ivan@example.com");

        // Реальный путь покупки: Source=PURCHASE, автор плана ≠ покупатель.
        await InvokeMessageAndWaitAsync(new PlanGrantCreated(
            GrantId: grantId,
            UserId: buyerId,
            PlanId: Guid.NewGuid(),
            PlanTier: "COURSE",
            PlanAuthorId: authorId,
            CourseId: Guid.NewGuid(),
            IncludesFutureContent: false,
            Source: "PURCHASE",
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: null,
            PlanName: "Курс по C#"));

        Notification buyerNotification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == buyerId));
        Notification authorNotification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.PlanGrantReceived, buyerNotification.Type);
        Assert.Equal(NotificationType.PlanGrantAuthorSale, authorNotification.Type);
        Assert.Equal(grantId, buyerNotification.CorrelationId);
        Assert.Equal(grantId, authorNotification.CorrelationId);
        // #445: автор видит «План/курс» и «Доступ» раздельно в тексте уведомления о продаже.
        Assert.Contains("Курс по C#", authorNotification.Body, StringComparison.Ordinal);
        Assert.Contains("Доступ:", authorNotification.Body, StringComparison.Ordinal);
    }

    private void MockBuyer(Guid userId, string name, string username, string email) =>
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(userId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(UserId: userId, Name: name, Username: username, Email: email, AvatarId: null)]));
}
