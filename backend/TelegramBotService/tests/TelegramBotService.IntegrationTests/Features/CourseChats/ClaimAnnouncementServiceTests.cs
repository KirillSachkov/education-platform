using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel;
using Telegram.Bot;
using Telegram.Bot.Requests.Abstractions;
using Telegram.Bot.Types;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Domain.CourseChats;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// ClaimAnnouncementService (#416): закреплённое claim-объявление постится и в закрытый КАНАЛ,
/// не только в группу. Pure-domain тесты (без БД) — мокаем <see cref="ITelegramBotClient"/>.
/// </summary>
public sealed class ClaimAnnouncementServiceTests
{
    private readonly ITelegramBotClient _bot = Substitute.For<ITelegramBotClient>();
    private readonly IAccessServiceClient _access = Substitute.For<IAccessServiceClient>();

    private ClaimAnnouncementService BuildService() =>
        new(_bot, _access, NullLogger<ClaimAnnouncementService>.Instance);

    private static ChatBinding ChannelBinding(bool membershipGrantsEnrollment) =>
        ChatBinding.Create(
            Guid.NewGuid(), Guid.NewGuid(), -100_500_900, ChatType.CHANNEL,
            "Закрытый канал", "https://t.me/+chan",
            enrollmentGrantsMembership: true,
            membershipGrantsEnrollment: membershipGrantsEnrollment,
            autoKickOnRevoke: false,
            enforceMembership: false,
            createdBy: Guid.NewGuid()).Value;

    [Fact]
    public async Task TryPostAndMark_ChannelWithReverseClaim_PostsAndMarksAnnouncement()
    {
        ChatBinding binding = ChannelBinding(membershipGrantsEnrollment: true);

        _bot.SendRequest(Arg.Any<IRequest<User>>(), Arg.Any<CancellationToken>())
            .Returns(new User { Id = 42, IsBot = true, FirstName = "Bot", Username = "testbot" });
        // Telegram.Bot v22: Message.Id — settable backing; MessageId — read-only alias (=> Id).
        _bot.SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>())
            .Returns(new Message { Id = 777 });
        _access.GetPlanTelegramInfoAsync(binding.PlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(binding.PlanId, "Backend-интенсив", "FULL_ALL", null)));

        await BuildService().TryPostAndMarkAsync(binding, CancellationToken.None);

        Assert.Equal(777, binding.AnnouncementMessageId);
        await _bot.Received().SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryPostAndMark_ReverseClaimDisabled_SkipsWithoutPosting()
    {
        ChatBinding binding = ChannelBinding(membershipGrantsEnrollment: false);

        await BuildService().TryPostAndMarkAsync(binding, CancellationToken.None);

        Assert.Null(binding.AnnouncementMessageId);
        await _bot.DidNotReceive().SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TryPostAndMark_AlreadyPosted_IsIdempotent()
    {
        ChatBinding binding = ChannelBinding(membershipGrantsEnrollment: true);
        binding.MarkAnnouncementPosted(123);

        await BuildService().TryPostAndMarkAsync(binding, CancellationToken.None);

        Assert.Equal(123, binding.AnnouncementMessageId);
        await _bot.DidNotReceive().SendRequest(Arg.Any<IRequest<Message>>(), Arg.Any<CancellationToken>());
    }
}
