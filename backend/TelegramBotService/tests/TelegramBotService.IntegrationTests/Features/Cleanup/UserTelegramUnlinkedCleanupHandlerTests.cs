using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using TelegramBotService.Core.Messaging.Consumers;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.IntegrationTests.Infrastructure;
using TelegramBotService.Infrastructure.Postgres;

namespace TelegramBotService.IntegrationTests.Features.Cleanup;

[Collection(nameof(TelegramBotTestCollection))]
public sealed class UserTelegramUnlinkedCleanupHandlerTests : TelegramBotTestsBase
{
    public UserTelegramUnlinkedCleanupHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_ExistingLink_RemovesIt()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555000001;

        await ExecuteInDb(async db =>
        {
            UserLink link = UserLink.Create(telegramUserId, platformUserId, "user").Value;
            await db.UserLinks.AddAsync(link);
            await db.SaveChangesAsync();
        });

        UserTelegramUnlinked evt = new(platformUserId, telegramUserId);

        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramUnlinkedCleanupHandler handler = new(
            BuildUserLinkRepository(db),
            LoggerFactory.CreateLogger<UserTelegramUnlinkedCleanupHandler>());

        await handler.Handle(evt, default);

        bool exists = await ExecuteInDb(d =>
            d.UserLinks.AnyAsync(x => x.TelegramUserId == telegramUserId));
        Assert.False(exists);
    }

    [Fact]
    public async Task Handle_NoLinkExists_Idempotent()
    {
        Guid platformUserId = Guid.NewGuid();
        long telegramUserId = 555000002;

        UserTelegramUnlinked evt = new(platformUserId, telegramUserId);

        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramUnlinkedCleanupHandler handler = new(
            BuildUserLinkRepository(db),
            LoggerFactory.CreateLogger<UserTelegramUnlinkedCleanupHandler>());

        // Не должен бросить — handler логирует debug и выходит.
        await handler.Handle(evt, default);

        bool exists = await ExecuteInDb(d =>
            d.UserLinks.AnyAsync(x => x.TelegramUserId == telegramUserId));
        Assert.False(exists);
    }

    [Fact]
    public async Task Handle_MultipleUnlinkEvents_AffectsOnlyMatchingTelegramId()
    {
        Guid userA = Guid.NewGuid();
        Guid userB = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(555_100_001, userA, "a").Value);
            await db.UserLinks.AddAsync(UserLink.Create(555_100_002, userB, "b").Value);
            await db.SaveChangesAsync();
        });

        // Unlink только юзера A
        await using TelegramBotDbContext db = BuildDbContext();
        UserTelegramUnlinkedCleanupHandler handler = new(
            BuildUserLinkRepository(db),
            LoggerFactory.CreateLogger<UserTelegramUnlinkedCleanupHandler>());

        await handler.Handle(new UserTelegramUnlinked(userA, 555_100_001), default);

        // User A ушёл, User B остался.
        bool aExists = await ExecuteInDb(d => d.UserLinks.AnyAsync(x => x.TelegramUserId == 555_100_001));
        bool bExists = await ExecuteInDb(d => d.UserLinks.AnyAsync(x => x.TelegramUserId == 555_100_002));
        Assert.False(aExists);
        Assert.True(bExists);
    }
}
