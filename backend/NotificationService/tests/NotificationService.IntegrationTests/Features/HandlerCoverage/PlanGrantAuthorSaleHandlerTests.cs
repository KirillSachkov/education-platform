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
/// L1 handler coverage для <c>PlanGrantAuthorSaleHandler</c> (#428, #856) — уведомление АВТОРУ
/// плана только о подтверждённой покупке с данными покупателя (имя, платформенный ник без @,
/// email), названием плана и объёмом доступа (#445 — «План/курс» и «Доступ» как разные сущности).
/// Sibling к
/// <see cref="PlanGrantReceivedHandlerTests"/> на той же очереди.
/// Регрессия #615: buyerLine не использует @-prefix для платформенного username — Telegram
/// рендерит @name как mention-ссылку на случайного TG-юзера. Канал Email убран из шаблона.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanGrantAuthorSaleHandlerTests : NotificationServiceTestsBase
{
    public PlanGrantAuthorSaleHandlerTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PlanGrantCreated_NotifiesAuthor_WithBuyerNickAndEmail()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Иван Петров", username: "ivan", email: "ivan@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, grantId, planTier: "COURSE", source: "PURCHASE", planName: "Тариф Профи"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Equal(NotificationType.PlanGrantAuthorSale, notification.Type);
        Assert.Equal(grantId, notification.CorrelationId);
        // Видны имя, ник (без @-prefix, #615) и email покупателя.
        Assert.Contains("Иван Петров", notification.Body, StringComparison.Ordinal);
        // Платформенный username показывается без @ — иначе Telegram рендерит это как mention
        // постороннего TG-юзера (#615).
        Assert.Contains("(ivan)", notification.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("@ivan", notification.Body, StringComparison.Ordinal);
        Assert.Contains("ivan@example.com", notification.Body, StringComparison.Ordinal);
        // #445: название плана и объём доступа — две разные сущности, обе в тексте под лейблами.
        Assert.Contains("Тариф Профи", notification.Body, StringComparison.Ordinal);
        Assert.Contains("План/курс:", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Доступ:", notification.Body, StringComparison.Ordinal);
        // #632: бессрочный грант (ExpiresAt=null) → срок «Навсегда».
        Assert.Contains("Срок: Навсегда", notification.Body, StringComparison.Ordinal);
        // payload несёт сырые поля покупателя + имя плана для фронта.
        Assert.Contains("ivan@example.com", notification.Payload, StringComparison.Ordinal);
        Assert.Contains("ivan", notification.Payload, StringComparison.Ordinal);
        Assert.Contains("Тариф Профи", notification.Payload, StringComparison.Ordinal);
        Assert.Equal(
            NotificationChannel.InApp | NotificationChannel.Telegram,
            notification.Channels);
    }

    [Theory]
    [InlineData("INVITE_LINK")]
    [InlineData("ADMIN_GRANT")]
    [InlineData("MIGRATION")]
    [InlineData("TRIAL")]
    [InlineData("GITHUB_ORG")]
    [InlineData("TELEGRAM_F1")]
    public async Task PlanGrantCreated_NonPurchaseSource_DoesNotNotifyAuthor(string source)
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Участник", username: "member", email: "member@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, Guid.NewGuid(), planTier: "FULL_ALL", source: source));

        int authorNotifications = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.Type == NotificationType.PlanGrantAuthorSale));

        Assert.Equal(0, authorNotifications);
    }

    [Fact]
    public async Task PlanGrantCreated_MigrationSource_DoesNotNotifyAuthor()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Backfill", username: "bf", email: "bf@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, Guid.NewGuid(), planTier: "FULL_ALL", source: "MIGRATION"));

        int authorNotifications = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.Type == NotificationType.PlanGrantAuthorSale));

        Assert.Equal(0, authorNotifications);
    }

    [Fact]
    public async Task PlanGrantCreated_SelfGrant_DoesNotNotifyAuthor()
    {
        Guid authorAndBuyer = Guid.NewGuid();
        MockBuyer(authorAndBuyer, name: "Author", username: "author", email: "author@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorAndBuyer, authorAndBuyer, Guid.NewGuid(), planTier: "FULL_ALL", source: "PURCHASE"));

        int authorNotifications = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.Type == NotificationType.PlanGrantAuthorSale));

        Assert.Equal(0, authorNotifications);
    }

    [Fact]
    public async Task PlanGrantCreated_Twice_IsIdempotent()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Иван", username: "ivan", email: "ivan@example.com");
        PlanGrantCreated evt = BuildEvent(authorId, buyerId, grantId, planTier: "LEARN_ALL", source: "PURCHASE");

        await InvokeMessageAndWaitAsync(evt);
        await InvokeMessageAndWaitAsync(evt);

        int count = await ExecuteInDb(db => db.Notifications
            .CountAsync(x => x.RecipientUserId == authorId && x.Type == NotificationType.PlanGrantAuthorSale));

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PlanGrantCreated_NullPlanName_FallsBackGracefully()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Иван", username: "ivan", email: "ivan@example.com");

        // Событие без имени плана (старый publisher / backward-compat envelope) → fallback, не пусто.
        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, Guid.NewGuid(), planTier: "FULL_ALL", source: "PURCHASE", planName: null));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Contains("План/курс: не указан", notification.Body, StringComparison.Ordinal);
        Assert.Contains("Доступ: направление .NET Fullstack", notification.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Регрессия #615: покупатель без username не получает @-prefix в buyerLine.
    /// </summary>
    [Fact]
    public async Task PlanGrantCreated_BuyerWithoutUsername_ShowsDisplayNameOnly()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Мария Иванова", username: null!, email: "maria@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, Guid.NewGuid(), planTier: "COURSE", source: "PURCHASE"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Contains("Мария Иванова", notification.Body, StringComparison.Ordinal);
        // Нет username → buyerLine = "Мария Иванова" без скобок и без @-prefix.
        Assert.DoesNotContain("(@", notification.Body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Регрессия #615: author sale не генерирует email-уведомление.
    /// Email убран из канальной матрицы шаблона — автор получает только InApp + Telegram.
    /// </summary>
    [Fact]
    public async Task PlanGrantCreated_AuthorSale_DoesNotCreateEmailDelivery()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Покупатель", username: "buyer", email: "buyer@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, Guid.NewGuid(), planTier: "COURSE", source: "PURCHASE"));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        // Email канал не должен быть в Channels bitmask (#615: автор видит продажу только InApp+Telegram).
        Assert.Equal(NotificationChannel.None, notification.Channels & NotificationChannel.Email);
    }

    /// <summary>
    /// #632: пробный месячный грант (#580 — <c>ExpiresAt = GrantedAt + 30 дней</c>) → автор видит
    /// «Срок: На месяц», а не «Навсегда». Признак берётся из <c>ExpiresAt</c> события.
    /// </summary>
    [Fact]
    public async Task PlanGrantCreated_TrialGrant_ShowsMonthTerm()
    {
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();
        Guid grantId = Guid.NewGuid();
        MockBuyer(buyerId, name: "Пробник", username: "trial", email: "trial@example.com");

        await InvokeMessageAndWaitAsync(BuildEvent(
            authorId, buyerId, grantId, planTier: "FULL_ALL", source: "PURCHASE",
            expiresAt: DateTimeOffset.UtcNow.AddDays(30)));

        Notification notification = await ExecuteInDb(db => db.Notifications
            .AsNoTracking()
            .SingleAsync(x => x.RecipientUserId == authorId));

        Assert.Contains("Срок: На месяц", notification.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Навсегда", notification.Body, StringComparison.Ordinal);
    }

    private void MockBuyer(Guid userId, string name, string? username, string email)
    {
        AuthServiceClient.GetUsersByIdsAsync(
                Arg.Is<IReadOnlyList<Guid>>(ids => ids.Contains(userId)),
                Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<AuthUserLookupDto>, Error>(
                [new AuthUserLookupDto(UserId: userId, Name: name, Username: username, Email: email, AvatarId: null)]));
    }

    private static PlanGrantCreated BuildEvent(
        Guid authorId,
        Guid userId,
        Guid grantId,
        string planTier,
        string source,
        string? planName = "Курс по C#",
        DateTimeOffset? expiresAt = null) =>
        new(
            GrantId: grantId,
            UserId: userId,
            PlanId: Guid.NewGuid(),
            PlanTier: planTier,
            PlanAuthorId: authorId,
            CourseId: planTier is "COURSE" ? Guid.NewGuid() : null,
            IncludesFutureContent: planTier is "FULL_ALL" or "LEARN_ALL",
            Source: source,
            SourceRef: null,
            GrantedAt: DateTimeOffset.UtcNow,
            ExpiresAt: expiresAt,
            PlanName: planName);
}
