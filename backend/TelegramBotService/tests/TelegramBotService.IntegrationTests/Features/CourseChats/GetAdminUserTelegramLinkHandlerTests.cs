using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.UserLinks;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// <c>GET /telegram/admin/users/{userId}/link/</c> (#444): admin/support view привязки Telegram.
/// linked → данные UserLink; нет привязки → <c>{ linked:false, ... }</c> (200, не ошибка).
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class GetAdminUserTelegramLinkHandlerTests : TelegramBotTestsBase
{
    public GetAdminUserTelegramLinkHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_LinkedUser_ReturnsLinkData()
    {
        Guid platformUserId = Guid.NewGuid();
        long tgUserId = 800_001;

        await ExecuteInDb(async db =>
        {
            await db.UserLinks.AddAsync(UserLink.Create(tgUserId, platformUserId, "alice").Value);
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext db = BuildDbContext();
        GetAdminUserTelegramLinkHandler handler = new(BuildUserLinkRepository(db));

        var result = await handler.Handle(new GetAdminUserTelegramLinkQuery(platformUserId), default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Linked);
        Assert.Equal(tgUserId, result.Value.TelegramUserId);
        Assert.Equal("alice", result.Value.TelegramUsername);
        Assert.NotNull(result.Value.LinkedAt);
    }

    [Fact]
    public async Task Handle_NoLink_ReturnsNotLinked()
    {
        await using TelegramBotDbContext db = BuildDbContext();
        GetAdminUserTelegramLinkHandler handler = new(BuildUserLinkRepository(db));

        var result = await handler.Handle(new GetAdminUserTelegramLinkQuery(Guid.NewGuid()), default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Linked);
        Assert.Null(result.Value.TelegramUserId);
        Assert.Null(result.Value.TelegramUsername);
        Assert.Null(result.Value.LinkedAt);
    }
}
