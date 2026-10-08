using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBotService.Core.Delivery;
using TelegramBotService.Core.Diagnostics;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.Notifications;

/// <summary>
/// Тесты handler'а на новой архитектуре delivery service. Handler делегирует фактическую
/// отправку в <see cref="ITelegramDeliveryService"/> (его в свою очередь покрывают свои
/// тесты), сам отвечает за: Telegram-bit чек, поиск UserLink, skip-расgistration, cleanup
/// UserLink при permanent ошибках (bot_blocked / chat_not_found).
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class NotificationCreatedTelegramHandlerTests : TelegramBotTestsBase
{
    private const short INAPP_BIT = 1;
    private const short TELEGRAM_BIT = 2;
    private const short EMAIL_BIT = 4;

    private readonly ITelegramDeliveryService _delivery;
    private readonly TelegramMetrics _metrics;

    public NotificationCreatedTelegramHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _delivery = Substitute.For<ITelegramDeliveryService>();
        IMeterFactory meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        _metrics = new TelegramMetrics(meterFactory);
    }

    [Fact]
    public async Task Handle_NoTelegramBitInChannels_DoesNothing()
    {
        NotificationCreated evt = BuildEvent(
            recipientUserId: Guid.NewGuid(),
            channels: INAPP_BIT | EMAIL_BIT,
            telegramBody: "irrelevant");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
        await _delivery.DidNotReceiveWithAnyArgs().RecordSkipAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UserHasNoTelegramLink_RecordsSkipWithNoUserLinkErrorCode()
    {
        Guid recipient = Guid.NewGuid();
        NotificationCreated evt = BuildEvent(
            recipientUserId: recipient,
            channels: INAPP_BIT | TELEGRAM_BIT,
            telegramBody: "Привет *мир*");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        // Skip публикуется через delivery service → notification_deliveries получает запись
        // с error_code=no_user_link. Раньше был silent return.
        await _delivery.Received(1).RecordSkipAsync(
            evt.NotificationId,
            recipient,
            chatId: 0,
            TelegramDeliveryErrorCodes.NO_USER_LINK,
            errorDetail: null,
            Arg.Any<CancellationToken>());

        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyTelegramBody_RecordsSkipWithEmptyBodyErrorCode()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 123456789;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: INAPP_BIT | TELEGRAM_BIT,
            telegramBody: null);

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        await _delivery.Received(1).RecordSkipAsync(
            evt.NotificationId,
            platformUserId,
            telegramUserId,
            TelegramDeliveryErrorCodes.EMPTY_BODY,
            errorDetail: null,
            Arg.Any<CancellationToken>());

        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_HappyPath_CallsDeliverAsyncWithMarkdownText()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 987654321;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Delivered(42));

        const string telegramBody = "📘 *Новый материал* — откройте курс";
        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: INAPP_BIT | TELEGRAM_BIT,
            telegramBody: telegramBody);

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        await _delivery.Received(1).DeliverAsync(
            evt.NotificationId,
            platformUserId,
            telegramUserId,
            telegramBody,
            keyboard: null,
            ParseMode.Markdown,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BodyWithTrailingMarkdownLink_StripsLinkAndPassesInlineButton()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555000111;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Delivered(43));

        const string body = "✅ Запись на курс *Test*\n\nДоступ к материалам уже открыт.\n\n[Открыть курс](https://sachkov-learn.net/n/abc-123)";
        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: INAPP_BIT | TELEGRAM_BIT,
            telegramBody: body);

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        await _delivery.Received(1).DeliverAsync(
            evt.NotificationId,
            platformUserId,
            telegramUserId,
            text: "✅ Запись на курс *Test*\n\nДоступ к материалам уже открыт.",
            keyboard: Arg.Is<InlineKeyboardMarkup?>(kb =>
                kb != null
                && kb.InlineKeyboard.Single().Single().Text == "Открыть курс"
                && kb.InlineKeyboard.Single().Single().Url == "https://sachkov-learn.net/n/abc-123"),
            ParseMode.Markdown,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BodyWithLocalhostLink_KeepsLinkInlineWithoutKeyboard()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555000222;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Delivered(44));

        const string body = "📥 *Новое решение на ревью*\n\nstudent → задача\n\n[Открыть на проверку](http://localhost/n/abc-123)";
        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: INAPP_BIT | TELEGRAM_BIT,
            telegramBody: body);

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        await _delivery.Received(1).DeliverAsync(
            evt.NotificationId,
            platformUserId,
            telegramUserId,
            text: body,
            keyboard: null,
            ParseMode.Markdown,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_BotBlockedOutcome_SoftBlocksUserLink()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 100000001;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Failed(TelegramDeliveryErrorCodes.BOT_BLOCKED, "blocked"));

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: TELEGRAM_BIT,
            telegramBody: "any");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        // Link остаётся (не delete'ится), но soft-blocked: BlockedAt != null,
        // BlockedReason = bot_blocked. Юзер вернётся через /start → unblock.
        UserLink? link = await ExecuteInDb(d =>
            d.UserLinks.SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId));
        Assert.NotNull(link);
        Assert.False(link!.IsActive);
        Assert.Equal(TelegramDeliveryErrorCodes.BOT_BLOCKED, link.BlockedReason);
    }

    [Fact]
    public async Task Handle_ChatNotFoundOutcome_SoftBlocksUserLink()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 100000002;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Failed(TelegramDeliveryErrorCodes.CHAT_NOT_FOUND, "not found"));

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: TELEGRAM_BIT,
            telegramBody: "any");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        UserLink? link = await ExecuteInDb(d =>
            d.UserLinks.SingleOrDefaultAsync(x => x.TelegramUserId == telegramUserId));
        Assert.NotNull(link);
        Assert.False(link!.IsActive);
        Assert.Equal(TelegramDeliveryErrorCodes.CHAT_NOT_FOUND, link.BlockedReason);
    }

    [Fact]
    public async Task Handle_AlreadySoftBlockedLink_RecordsSkipWithoutAttemptingSend()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 100000005;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        // Заблокирована заранее (предыдущий цикл доставки получил bot_blocked).
        await ExecuteInDb(async d =>
        {
            UserLink link = await d.UserLinks.SingleAsync(x => x.TelegramUserId == telegramUserId);
            link.Block(TelegramDeliveryErrorCodes.BOT_BLOCKED);
            await d.SaveChangesAsync();
        });

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: TELEGRAM_BIT,
            telegramBody: "any");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        // Send не пытается — публикуется skip event.
        await _delivery.DidNotReceiveWithAnyArgs().DeliverAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
            Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());

        await _delivery.Received(1).RecordSkipAsync(
            evt.NotificationId,
            platformUserId,
            telegramUserId,
            TelegramDeliveryErrorCodes.BLOCKED_LINK,
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FloodControlOutcome_DoesNotRemoveUserLink()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 100000003;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Failed(TelegramDeliveryErrorCodes.FLOOD_CONTROL, "429"));

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: TELEGRAM_BIT,
            telegramBody: "any");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await Assert.ThrowsAsync<SharedKernel.Exceptions.TransientException>(
            () => handler.Handle(evt, default));

        bool linkExists = await ExecuteInDb(d =>
            d.UserLinks.AnyAsync(x => x.TelegramUserId == telegramUserId));
        Assert.True(linkExists);
    }

    [Fact]
    public async Task Handle_GenericApiErrorOutcome_DoesNotRemoveUserLink()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 100000004;
        await SeedUserLinkAsync(platformUserId, telegramUserId);

        _delivery.DeliverAsync(
                Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<long>(),
                Arg.Any<string>(), Arg.Any<InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(TelegramDeliveryOutcome.Failed(TelegramDeliveryErrorCodes.API_ERROR, "500"));

        NotificationCreated evt = BuildEvent(
            recipientUserId: platformUserId,
            channels: TELEGRAM_BIT,
            telegramBody: "any");

        await using TelegramBotDbContext db = BuildDbContext();
        NotificationCreatedTelegramHandler handler = BuildHandler(db);

        await handler.Handle(evt, default);

        bool linkExists = await ExecuteInDb(d =>
            d.UserLinks.AnyAsync(x => x.TelegramUserId == telegramUserId));
        Assert.True(linkExists);
    }

    private NotificationCreatedTelegramHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            BuildUserLinkRepository(db),
            BuildTransactionManager(db),
            _delivery,
            _metrics,
            NullLogger<NotificationCreatedTelegramHandler>.Instance);

    private async Task SeedUserLinkAsync(Guid platformUserId, long telegramUserId)
    {
        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "test_user").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });
    }

    private static NotificationCreated BuildEvent(
        Guid recipientUserId, short channels, string? telegramBody) =>
        new(
            NotificationId: Guid.NewGuid(),
            RecipientUserId: recipientUserId,
            Type: 3,
            Channels: channels,
            TemplateId: "material.published",
            Title: "Заголовок",
            Body: "InApp body",
            TelegramBody: telegramBody,
            PayloadJson: "{}",
            CorrelationId: null,
            CreatedAt: DateTimeOffset.UtcNow);
}
