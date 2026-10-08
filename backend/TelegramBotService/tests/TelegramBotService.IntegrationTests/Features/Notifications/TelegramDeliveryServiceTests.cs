using System.Diagnostics.Metrics;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shared.Messaging.IntegrationEvents.Notifications.Events;
using SharedKernel;
using SharedKernel.Exceptions;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Delivery;
using TelegramBotService.Core.Diagnostics;
using TelegramBotService.IntegrationTests.Infrastructure;
using TelegramBotService.Infrastructure.Postgres;

namespace TelegramBotService.IntegrationTests.Features.Notifications;

/// <summary>
/// Тесты на <see cref="TelegramDeliveryService"/> — главный фикс P0 для дублей и
/// слепого пятна по Telegram delivery. Покрывают: idempotency hit, success path,
/// классификацию ошибок Telegram API в стабильные error_code'ы, публикацию
/// <see cref="TelegramDeliveryRecorded"/> для каждого outcome'а.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class TelegramDeliveryServiceTests : TelegramBotTestsBase
{
    private readonly InMemoryTelegramIdempotencyStore _store;
    private readonly StubOutbox _outbox;

    public TelegramDeliveryServiceTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _store = new InMemoryTelegramIdempotencyStore();
        _outbox = new StubOutbox();
    }

    [Fact]
    public async Task DeliverAsync_FirstAttempt_SendsAndCachesAndPublishesDelivered()
    {
        Guid notificationId = Guid.NewGuid();
        Guid recipientUserId = Guid.NewGuid();
        long chatId = 1000;

        BotNotifier.SendTextAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 555 });

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            notificationId, recipientUserId, chatId,
            text: "Hi", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsDelivered);
        Assert.Equal(555, outcome.ProviderMessageId);

        // Cache hit на следующий вызов.
        int? cached = await _store.TryGetSentMessageIdAsync(notificationId, chatId, default);
        Assert.Equal(555, cached);

        // Опубликован TelegramDeliveryRecorded со status=delivered.
        TelegramDeliveryRecorded published = Assert.IsType<TelegramDeliveryRecorded>(_outbox.Sent.Single());
        Assert.Equal(TelegramDeliveryStatuses.DELIVERED, published.Status);
        Assert.Equal("555", published.ProviderMessageId);
        Assert.Null(published.ErrorCode);
    }

    [Fact]
    public async Task DeliverAsync_IdempotencyHit_DoesNotResendAndPublishesAlreadySent()
    {
        // Предыдущая отправка успешно прошла — ключ в кэше.
        Guid notificationId = Guid.NewGuid();
        Guid recipientUserId = Guid.NewGuid();
        long chatId = 1001;
        await _store.SaveSentMessageIdAsync(notificationId, chatId, 777, default);

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            notificationId, recipientUserId, chatId,
            text: "Hi (retry)", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsSkipped);
        Assert.Equal(777, outcome.ProviderMessageId);
        Assert.Equal(TelegramDeliveryErrorCodes.ALREADY_SENT, outcome.ErrorCode);

        // SendTextAsync **не** должен был вызваться второй раз — это и есть защита от дублей.
        await BotNotifier.DidNotReceiveWithAnyArgs().SendTextAsync(
            Arg.Any<long>(), Arg.Any<string>(),
            Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
            Arg.Any<ParseMode>(), Arg.Any<CancellationToken>());

        TelegramDeliveryRecorded published = Assert.IsType<TelegramDeliveryRecorded>(_outbox.Sent.Single());
        Assert.Equal(TelegramDeliveryStatuses.SKIPPED, published.Status);
        Assert.Equal(TelegramDeliveryErrorCodes.ALREADY_SENT, published.ErrorCode);
    }

    [Fact]
    public async Task DeliverAsync_BotBlocked_ClassifiesAsBotBlockedAndPublishesFailed()
    {
        BotNotifier.SendTextAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Forbidden: bot was blocked by the user", 403));

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            Guid.NewGuid(), Guid.NewGuid(), chatId: 2000,
            text: "Hi", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsFailed);
        Assert.Equal(TelegramDeliveryErrorCodes.BOT_BLOCKED, outcome.ErrorCode);

        TelegramDeliveryRecorded published = Assert.IsType<TelegramDeliveryRecorded>(_outbox.Sent.Single());
        Assert.Equal(TelegramDeliveryStatuses.FAILED, published.Status);
        Assert.Equal(TelegramDeliveryErrorCodes.BOT_BLOCKED, published.ErrorCode);
    }

    [Fact]
    public async Task DeliverAsync_ChatNotFound_ClassifiesAsChatNotFound()
    {
        BotNotifier.SendTextAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Bad Request: chat not found", 400));

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            Guid.NewGuid(), Guid.NewGuid(), chatId: 2001,
            text: "Hi", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsFailed);
        Assert.Equal(TelegramDeliveryErrorCodes.CHAT_NOT_FOUND, outcome.ErrorCode);
    }

    [Fact]
    public async Task DeliverAsync_FloodControl_ClassifiesAsFloodControl()
    {
        BotNotifier.SendTextAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Too Many Requests: retry after 30", 429));

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            Guid.NewGuid(), Guid.NewGuid(), chatId: 2002,
            text: "Hi", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsFailed);
        Assert.Equal(TelegramDeliveryErrorCodes.FLOOD_CONTROL, outcome.ErrorCode);
    }

    [Fact]
    public async Task DeliverAsync_OtherApiError_ClassifiesAsApiError()
    {
        BotNotifier.SendTextAsync(
                Arg.Any<long>(), Arg.Any<string>(),
                Arg.Any<Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup?>(),
                Arg.Any<ParseMode>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiRequestException("Internal Server Error", 500));

        TelegramDeliveryService svc = BuildService();
        TelegramDeliveryOutcome outcome = await svc.DeliverAsync(
            Guid.NewGuid(), Guid.NewGuid(), chatId: 2003,
            text: "Hi", keyboard: null, parseMode: ParseMode.Markdown,
            ct: default);

        Assert.True(outcome.IsFailed);
        Assert.Equal(TelegramDeliveryErrorCodes.API_ERROR, outcome.ErrorCode);
    }

    [Fact]
    public async Task RecordSkipAsync_PublishesSkippedEventWithGivenErrorCode()
    {
        TelegramDeliveryService svc = BuildService();
        Guid nid = Guid.NewGuid();
        Guid uid = Guid.NewGuid();

        await svc.RecordSkipAsync(nid, uid, chatId: 3000,
            errorCode: TelegramDeliveryErrorCodes.NO_USER_LINK,
            errorDetail: null, ct: default);

        TelegramDeliveryRecorded published = Assert.IsType<TelegramDeliveryRecorded>(_outbox.Sent.Single());
        Assert.Equal(TelegramDeliveryStatuses.SKIPPED, published.Status);
        Assert.Equal(TelegramDeliveryErrorCodes.NO_USER_LINK, published.ErrorCode);
        Assert.Equal(nid, published.NotificationId);
        Assert.Equal(uid, published.RecipientUserId);
        Assert.Equal(3000, published.ChatId);
    }

    [Fact]
    public async Task RecordSkipAsync_WhenOutboxSaveFails_ThrowsTransient()
    {
        ITransactionManager transactions = Substitute.For<ITransactionManager>();
        transactions.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(UnitResult.Failure<Error>(GeneralErrors.DatabaseError()));
        TelegramDeliveryService svc = BuildService(transactions);

        await Assert.ThrowsAsync<TransientException>(() => svc.RecordSkipAsync(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            chatId: 3001,
            errorCode: TelegramDeliveryErrorCodes.NO_USER_LINK,
            errorDetail: null,
            ct: default));
    }

    private TelegramDeliveryService BuildService(ITransactionManager? transactions = null)
    {
        TelegramBotDbContext db = BuildDbContext();
        IMeterFactory meterFactory = new ServiceCollection().AddMetrics().BuildServiceProvider()
            .GetRequiredService<IMeterFactory>();
        return new TelegramDeliveryService(
            BotNotifier,
            _store,
            new NoopBotThrottler(),
            _outbox,
            transactions ?? new TestTransactionManager(db),
            new TelegramMetrics(meterFactory),
            NullLogger<TelegramDeliveryService>.Instance);
    }
}

internal sealed class StubOutbox : IOutboxService
{
    public List<object> Sent { get; } = [];

    public Task PublishAsync<T>(T message) where T : class
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }

    public Task FlushAsync() => Task.CompletedTask;
}
