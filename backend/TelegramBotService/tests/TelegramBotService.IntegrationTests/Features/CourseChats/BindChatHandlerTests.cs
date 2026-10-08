using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using Telegram.Bot;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.Requests;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotService.Core.Features.CourseChats.UseCases;
using TelegramBotService.Domain;
using TelegramBotService.Domain.CourseChats;
using TelegramBotService.Infrastructure.Postgres;
using TelegramBotService.IntegrationTests.Infrastructure;

namespace TelegramBotService.IntegrationTests.Features.CourseChats;

/// <summary>
/// BindChatHandler (admin endpoint <c>POST /telegram/admin/plans/{planId}/chat-bindings/</c>):
/// reachability, права бота, idempotent rebind, outbox publish ChatBindingBoundToPlan.
/// </summary>
[Collection(nameof(TelegramBotTestCollection))]
public sealed class BindChatHandlerTests : TelegramBotTestsBase
{
    private readonly BindChatCommandValidator _validator = new();
    private readonly UserScopedData _user;
    private readonly IAccessServiceClient _accessClient = Substitute.For<IAccessServiceClient>();

    public BindChatHandlerTests(TelegramBotTestFixture fixture) : base(fixture)
    {
        _user = new UserScopedData();
        _user.Authenticate(Guid.NewGuid(), "admin", "a@x.io", ["platform-admin"]);
        _accessClient.GetPlanTelegramInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    call.Arg<Guid>(), "Plan", "FULL_ALL", null, call.Arg<Guid>(),
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));
    }

    [Fact]
    public async Task Handle_HappyPath_CreatesBindingAndPublishesEvent()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -4001;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "supergroup", "My chat", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: true, CanManageChat: true)));
        ChatApi.CreateJoinRequestInviteLinkAsync(chatId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<string>.Success("https://t.me/+invite"));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId, new BindChatRequest(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(chatId, result.Value.TelegramChatId);
        Assert.Equal("https://t.me/+invite", result.Value.InviteLink);

        // Outbox publish — Pattern A через TestOutboxCollector.
        ChatBindingBoundToPlan ev = OutboxCollector.OfType<ChatBindingBoundToPlan>().Single();
        Assert.Equal(planId, ev.PlanId);
        Assert.Equal(chatId, ev.TelegramChatId);
        Assert.Equal(result.Value.Id, ev.BindingId);
    }

    [Fact]
    public async Task Handle_EnrollmentBindingForPlanWithoutCommunityCapability_IsRejected()
    {
        Guid planId = Guid.CreateVersion7();
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    planId, "Learn only", "COURSE", null, planId, ["VIEW_MATERIALS"])));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        Result<ChatBindingDto, Error> result = await handler.Handle(
            new BindChatCommand(planId, new BindChatRequest("-4009")),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("telegram.chat.plan.community_access_required", result.Error.Messages[0].Code);
        await ChatApi.DidNotReceiveWithAnyArgs().GetChatAsync(
            Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TrialPlanBinding_IsRejectedInFavorOfCanonicalPlan()
    {
        Guid trialPlanId = Guid.CreateVersion7();
        Guid canonicalPlanId = Guid.CreateVersion7();
        _accessClient.GetPlanTelegramInfoAsync(trialPlanId, Arg.Any<CancellationToken>())
            .Returns(Result.Success<PlanTelegramInfoDto, Error>(
                new PlanTelegramInfoDto(
                    trialPlanId, "Trial", "FULL_ALL", null, canonicalPlanId,
                    [TelegramGrantChatAccessResolver.COMMUNITY_ACCESS])));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        Result<ChatBindingDto, Error> result = await handler.Handle(
            new BindChatCommand(trialPlanId, new BindChatRequest("-4010")),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("telegram.chat.plan.canonical_required", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task Handle_ChatNotReachable_ReturnsErrorWithoutOutbox()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -4002;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Failure(ChatApiErrorCode.ChatNotReachable));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId, new BindChatRequest(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(TelegramBotErrors.ChatNotReachable().Messages[0].Code, result.Error.Messages[0].Code);
        Assert.Empty(OutboxCollector.OfType<ChatBindingBoundToPlan>());
    }

    [Fact]
    public async Task Handle_PlanNotFound_ReturnsErrorBeforeCreatingInvite()
    {
        Guid planId = Guid.CreateVersion7();
        _accessClient.GetPlanTelegramInfoAsync(planId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure<PlanTelegramInfoDto, Error>(
                Error.NotFound("plan.not_found", "Plan not found")));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        Result<ChatBindingDto, Error> result = await handler.Handle(
            new BindChatCommand(planId, new BindChatRequest("-9999")),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.not_found", result.Error.Messages[0].Code);
        await ChatApi.DidNotReceiveWithAnyArgs().CreateJoinRequestInviteLinkAsync(
            default, default, default);
        Assert.Equal(0, await ExecuteInDb(context => context.ChatBindings.CountAsync()));
    }

    [Fact]
    public async Task Handle_BotMissingRights_ReturnsBotMissingRightsError()
    {
        Guid planId = Guid.NewGuid();
        long chatId = -4003;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "supergroup", "X", null)));
        // Бот админ, но без can_invite_users.
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: false,
                    CanRestrictMembers: true, CanManageChat: true)));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId, new BindChatRequest(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("telegram.chat.bot.missing.rights", result.Error.Messages[0].Code);
        Assert.Empty(OutboxCollector.OfType<ChatBindingBoundToPlan>());
    }

    [Fact]
    public async Task Handle_IdempotentRebind_UpdatesFlagsWithoutPublishingEvent()
    {
        // Существующий binding на тот же (plan, chat) → handler обновляет флаги +
        // НЕ публикует ChatBindingBoundToPlan (event только при первом bind).
        Guid planId = Guid.NewGuid();
        long chatId = -4004;

        await ExecuteInDb(async db =>
        {
            ChatBinding existing = ChatBinding.Create(
                Guid.NewGuid(), planId, chatId, ChatType.SUPERGROUP,
                "Old title", "https://t.me/+old",
                enrollmentGrantsMembership: true,
                membershipGrantsEnrollment: true,
                autoKickOnRevoke: false,
                enforceMembership: false,
                createdBy: Guid.NewGuid()).Value;
            await db.ChatBindings.AddAsync(existing);
            await db.SaveChangesAsync();
        });

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "supergroup", "New title", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: true, CanManageChat: true)));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(
                    chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EnrollmentGrantsMembership: false,
                    MembershipGrantsEnrollment: false,
                    AutoKickOnRevoke: true)),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EnrollmentGrantsMembership);
        Assert.True(result.Value.AutoKickOnRevoke);

        // No second binding created.
        int totalBindings = await ExecuteInDb(d => d.ChatBindings.CountAsync(b => b.PlanId == planId));
        Assert.Equal(1, totalBindings);

        // No outbox event на rebind (только первый bind триггерит).
        Assert.Empty(OutboxCollector.OfType<ChatBindingBoundToPlan>());
    }

    [Fact]
    public async Task Handle_ChannelChatType_AllowsMembershipGrantsEnrollmentButClampsEnforceMembership()
    {
        // Channel reverse-claim теперь разрешён (#416) — MembershipGrantsEnrollment сохраняется.
        // EnforceMembership остаётся group-only (нет ChatMember updates для subscribers) → clamped.
        Guid planId = Guid.NewGuid();
        long chatId = -4005;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "channel", "News channel", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: true, CanManageChat: true)));
        ChatApi.CreateJoinRequestInviteLinkAsync(chatId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<string>.Success("https://t.me/+channel"));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(
                    chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EnrollmentGrantsMembership: true,
                    MembershipGrantsEnrollment: true,   // client requested true
                    AutoKickOnRevoke: false,
                    EnforceMembership: true)),          // client requested true
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.MembershipGrantsEnrollment);    // preserved for channel (#416)
        Assert.False(result.Value.EnforceMembership);            // clamped — group-only
        Assert.True(result.Value.EnrollmentGrantsMembership);    // preserved
    }

    [Fact]
    public async Task Handle_ChannelWithoutCanRestrictMembers_BindsSuccessfully()
    {
        // Закрытый канал (#442): can_restrict_members нужен только для F6 auto-kick (best-effort).
        // Bind канала, где бот админ с Post/Invite, но без права «банить», НЕ должен падать —
        // владельцы каналов часто не дают боту Ban. can_invite_users остаётся обязательным.
        Guid planId = Guid.NewGuid();
        long chatId = -100_500_777;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "channel", "Closed channel", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: false, CanManageChat: true)));   // нет права банить
        ChatApi.CreateJoinRequestInviteLinkAsync(chatId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<string>.Success("https://t.me/+closedchan"));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(
                    chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    EnrollmentGrantsMembership: true,
                    MembershipGrantsEnrollment: true)),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(chatId, result.Value.TelegramChatId);
        Assert.True(result.Value.MembershipGrantsEnrollment);
    }

    [Fact]
    public async Task Handle_SupergroupWithoutCanRestrictMembers_ReturnsBotMissingRights()
    {
        // Регресс-гард: для group/supergroup требование can_restrict_members сохраняется (#442) —
        // только каналы освобождены. Без него bind группы по-прежнему 422.
        Guid planId = Guid.NewGuid();
        long chatId = -4006;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "supergroup", "Group", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: false, CanManageChat: true)));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(chatId.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("telegram.chat.bot.missing.rights", result.Error.Messages[0].Code);
        Assert.Empty(OutboxCollector.OfType<ChatBindingBoundToPlan>());
    }

    [Fact]
    public async Task Handle_SupergroupBind_DefaultsAutoKickOnRevokeTrue()
    {
        // #687: курсовая ГРУППА авто-кикает на revoke/expire по умолчанию — даже если запрос
        // (UI/MCP) шлёт AutoKickOnRevoke=false / опускает его.
        Guid planId = Guid.NewGuid();
        long chatId = -4101;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "supergroup", "Course group", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: true, CanManageChat: true)));
        ChatApi.CreateJoinRequestInviteLinkAsync(chatId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<string>.Success("https://t.me/+group"));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(
                    chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AutoKickOnRevoke: false)),   // client sends false — overridden for SUPERGROUP
            default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AutoKickOnRevoke);
    }

    [Fact]
    public async Task Handle_ChannelBind_KeepsAutoKickOnRevokeOptIn()
    {
        // CHANNEL сохраняет opt-in (kick на канале не поддерживается) — запрос false остаётся false.
        Guid planId = Guid.NewGuid();
        long chatId = -4102;

        ChatApi.GetChatAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<ChatInfo>.Success(new ChatInfo(chatId, "channel", "Course channel", null)));
        ChatApi.GetBotPermissionsAsync(chatId, Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<BotChatPermissions>.Success(
                new BotChatPermissions(IsAdministrator: true, CanInviteUsers: true,
                    CanRestrictMembers: false, CanManageChat: true)));
        ChatApi.CreateJoinRequestInviteLinkAsync(chatId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ChatApiResult<string>.Success("https://t.me/+chan"));

        await using TelegramBotDbContext db = BuildDbContext();
        BindChatHandler handler = BuildHandler(db);

        var result = await handler.Handle(
            new BindChatCommand(planId,
                new BindChatRequest(
                    chatId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    AutoKickOnRevoke: false)),
            default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AutoKickOnRevoke);
    }

    private BindChatHandler BuildHandler(TelegramBotDbContext db) =>
        new(
            _validator,
            new ChatBindingRepository(db),
            _accessClient,
            ChatApi,
            BuildTransactionManager(db),
            BuildOutboxService(),
            new ClaimAnnouncementService(
                Substitute.For<ITelegramBotClient>(),
                _accessClient,
                NullLogger<ClaimAnnouncementService>.Instance),
            _user,
            NullLogger<BindChatHandler>.Instance);
}
