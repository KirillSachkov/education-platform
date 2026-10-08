using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;
using Wolverine.Testing;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// UnbindChatHandler: 404 для несуществующего binding, happy path + ChatBindingUnboundFromPlan
/// с правильным RemainingBindingsCount.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class UnbindChatHandlerTests : TelegramBotTestsBase
{
    private readonly UserScopedData _user;

    public UnbindChatHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _user = new UserScopedData();
        _user.Authenticate(Guid.NewGuid(), "admin", "a@x.io", ["platform-admin"]);
    }

    [Fact]
    public async Task Handle_BindingNotFound_ReturnsError()
    {
        await using TelegramBotDbContext db = BuildDbContext();
        UnbindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(new UnbindChatCommand(Guid.NewGuid()), default);

        Assert.True(result.IsFailure);
        Assert.Equal(TelegramBotErrors.ChatBindingNotFound().Messages[0].Code,
            result.Error.Messages[0].Code);
        Assert.Empty(OutboxCollector.OfType<ChatBindingUnboundFromPlan>());
    }

    [Fact]
    public async Task Handle_HappyPath_RemovesBindingAndPublishesEvent()
    {
        Guid planId = Guid.NewGuid();
        Guid bindingId = Guid.NewGuid();
        long chatId = -5001;

        await ExecuteInDb(async db =>
        {
            ChatBinding b = ChatBinding.Create(
                bindingId, planId, chatId, ChatType.SUPERGROUP,
                "X", "https://t.me/+abc",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: false,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(b);
            await db.SaveChangesAsync();
        });

        ChatApi.RevokeChatInviteLinkAsync(chatId, "https://t.me/+abc", Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<bool>.Success(true));

        await using TelegramBotDbContext db = BuildDbContext();
        UnbindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(new UnbindChatCommand(bindingId), default);

        Assert.True(result.IsSuccess);
        bool exists = await ExecuteInDb(d => d.ChatBindings.AnyAsync(b => b.Id == bindingId));
        Assert.False(exists);

        ChatBindingUnboundFromPlan ev = OutboxCollector.OfType<ChatBindingUnboundFromPlan>().Single();
        Assert.Equal(bindingId, ev.BindingId);
        Assert.Equal(planId, ev.PlanId);
        Assert.Equal(chatId, ev.TelegramChatId);
        Assert.Equal(0, ev.RemainingBindingsCount);  // последний binding плана
    }

    [Fact]
    public async Task Handle_OneOfMultipleBindings_PublishesRemainingCountOne()
    {
        Guid planId = Guid.NewGuid();
        Guid removeId = Guid.NewGuid();
        Guid keepId = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            await db.ChatBindings.AddAsync(MakeBinding(removeId, planId, -5101));
            await db.ChatBindings.AddAsync(MakeBinding(keepId, planId, -5102));
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext db = BuildDbContext();
        UnbindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(new UnbindChatCommand(removeId), default);

        Assert.True(result.IsSuccess);
        ChatBindingUnboundFromPlan ev = OutboxCollector.OfType<ChatBindingUnboundFromPlan>().Single();
        Assert.Equal(1, ev.RemainingBindingsCount);
    }

    [Fact]
    public async Task Handle_ConcurrentLastUnbinds_PublishCountsOneThenZero()
    {
        Guid planId = Guid.CreateVersion7();
        Guid firstId = Guid.CreateVersion7();
        Guid secondId = Guid.CreateVersion7();
        await ExecuteInDb(async db =>
        {
            await db.ChatBindings.AddRangeAsync(
                MakeBinding(firstId, planId, -5201),
                MakeBinding(secondId, planId, -5202));
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext firstDb = BuildDbContext();
        await using TelegramBotDbContext secondDb = BuildDbContext();
        var firstCollector = new TestOutboxCollector();
        var secondCollector = new TestOutboxCollector();
        UnbindChatHandler first = BuildHandler(firstDb, firstCollector);
        UnbindChatHandler second = BuildHandler(secondDb, secondCollector);

        var results = await Task.WhenAll(
            first.Handle(new UnbindChatCommand(firstId), default),
            second.Handle(new UnbindChatCommand(secondId), default));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        int[] remainingCounts = firstCollector.OfType<ChatBindingUnboundFromPlan>()
            .Concat(secondCollector.OfType<ChatBindingUnboundFromPlan>())
            .Select(evt => evt.RemainingBindingsCount)
            .Order()
            .ToArray();
        Assert.Equal([0, 1], remainingCounts);
        Assert.Equal(0, await ExecuteInDb(db => db.ChatBindings.CountAsync(x => x.PlanId == planId)));
    }

    private UnbindChatHandler BuildHandler(TelegramBotDbContext db) =>
        BuildHandler(db, OutboxCollector);

    private UnbindChatHandler BuildHandler(TelegramBotDbContext db, TestOutboxCollector collector) =>
        new(
            new ChatBindingRepository(db),
            ChatApi,
            BuildTransactionManager(db),
            new TestOutboxService(collector),
            _user,
            NullLogger<UnbindChatHandler>.Instance);

    private static ChatBinding MakeBinding(Guid id, Guid planId, long chatId) =>
        ChatBinding.Create(
            id, planId, chatId, ChatType.SUPERGROUP,
            "X", "https://t.me/+abc",
            enrollmentGrantsMembership: true,
            membershipGrantsEnrollment: false,
            autoKickOnRevoke: false,
            enforceMembership: false,
            createdBy: Guid.NewGuid()).Value;
}
