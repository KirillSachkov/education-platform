using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// ListChatBindings: query handler возвращает только binding'и переданного плана.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class ListChatBindingsHandlerTests : TelegramBotTestsBase
{
    public ListChatBindingsHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Handle_ReturnsOnlyBindingsOfRequestedPlan()
    {
        Guid planA = Guid.NewGuid();
        Guid planB = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            await db.ChatBindings.AddAsync(MakeBinding(planA, -8001));
            await db.ChatBindings.AddAsync(MakeBinding(planA, -8002));
            await db.ChatBindings.AddAsync(MakeBinding(planB, -8003));
            await db.SaveChangesAsync();
        });

        await using TelegramBotDbContext db = BuildDbContext();
        ListChatBindingsHandler handler = new(new ChatBindingRepository(db));

        var result = await handler.Handle(new ListChatBindingsQuery(planA), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.All(result.Value, dto => Assert.Equal(planA, dto.PlanId));
    }

    private static ChatBinding MakeBinding(Guid planId, long chatId) =>
        ChatBinding.Create(
            Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
            "X", "https://t.me/+abc",
            enrollmentGrantsMembership: true,
            membershipGrantsEnrollment: false,
            autoKickOnRevoke: false,
            enforceMembership: false,
            createdBy: Guid.NewGuid()).Value;
}
