using System.Globalization;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.Plans.Dtos;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Telegram.Events;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;
using TelegramBotService.Contracts.Requests;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.CourseChats.Services;
using TelegramBotFlow.Core.Messaging;
using TelegramBotService.Domain;
using TelegramBotService.Domain.CourseChats;
using ChatType = TelegramBotService.Domain.CourseChats.ChatType;

namespace TelegramBotService.Core.Features.CourseChats.UseCases;

public sealed record BindChatCommand(Guid PlanId, BindChatRequest Request) : ICommand;

public sealed class BindChatCommandValidator : AbstractValidator<BindChatCommand>
{
    public BindChatCommandValidator()
    {
        RuleFor(x => x.PlanId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(BindChatCommand.PlanId)));

        RuleFor(x => x.Request.ChatIdentifier)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(BindChatRequest.ChatIdentifier)));
    }
}

public sealed class BindChatEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/telegram/admin/plans/{planId:guid}/chat-bindings/",
                async Task<EndpointResult<ChatBindingDto>> (
                    [FromRoute] Guid planId,
                    [FromBody] BindChatRequest request,
                    [FromServices] BindChatHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new BindChatCommand(planId, request), cancellationToken))
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed class BindChatHandler : ICommandHandler<ChatBindingDto, BindChatCommand>
{
    private readonly IValidator<BindChatCommand> _validator;
    private readonly IChatBindingRepository _bindings;
    private readonly IAccessServiceClient _accessClient;
    private readonly IChatAdministrationApi _chatApi;
    private readonly ITransactionManager _transactions;
    private readonly IOutboxService _outbox;
    private readonly ClaimAnnouncementService _announcer;
    private readonly UserScopedData _user;
    private readonly ILogger<BindChatHandler> _logger;

    public BindChatHandler(
        IValidator<BindChatCommand> validator,
        IChatBindingRepository bindings,
        IAccessServiceClient accessClient,
        IChatAdministrationApi chatApi,
        ITransactionManager transactions,
        IOutboxService outbox,
        ClaimAnnouncementService announcer,
        UserScopedData user,
        ILogger<BindChatHandler> logger)
    {
        _validator = validator;
        _bindings = bindings;
        _accessClient = accessClient;
        _chatApi = chatApi;
        _transactions = transactions;
        _outbox = outbox;
        _announcer = announcer;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<ChatBindingDto, Error>> Handle(
        BindChatCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<PlanTelegramInfoDto, Error> planResult =
            await _accessClient.GetPlanTelegramInfoAsync(command.PlanId, cancellationToken);
        if (planResult.IsFailure)
            return planResult.Error;
        if (planResult.Value.CanonicalTelegramPlanId != command.PlanId)
            return CanonicalPlanRequired(planResult.Value.CanonicalTelegramPlanId);
        if (command.Request.EnrollmentGrantsMembership && !HasCommunityAccess(planResult.Value))
            return CommunityAccessRequired();

        ChatApiResult<ChatInfo> chatResult = await ResolveChatAsync(command.Request.ChatIdentifier, cancellationToken);
        if (chatResult.IsFailure)
            return TelegramBotErrors.ChatNotReachable();

        ChatInfo chat = chatResult.Value!;

        ChatType? chatType = chat.Type switch
        {
            "supergroup" => ChatType.SUPERGROUP,
            "channel" => ChatType.CHANNEL,
            _ => null
        };

        if (chatType is null)
            return TelegramBotErrors.InvalidChatType(chat.Type);

        bool isChannel = chatType.Value == ChatType.CHANNEL;

        ChatApiResult<BotChatPermissions> permsResult =
            await _chatApi.GetBotPermissionsAsync(chat.Id, cancellationToken);
        if (permsResult.IsFailure)
            return TelegramBotErrors.ChatNotReachable();

        BotChatPermissions perms = permsResult.Value!;
        if (!perms.IsAdministrator)
            return TelegramBotErrors.BotNotAdmin();

        List<string> missing = [];
        if (!perms.CanInviteUsers) missing.Add("can_invite_users");
        // can_restrict_members нужно ТОЛЬКО для F6 auto-kick (ban+unban). Для закрытого КАНАЛА
        // reverse-claim flow его не требует, а владельцы часто дают боту лишь Post/Invite без
        // права «банить» — поэтому для CHANNEL не делаем его hard-требованием bind'а. Auto-kick
        // на канале остаётся best-effort: при нехватке прав KickChatMemberAsync молча залогирует
        // фейл (PlanGrantRevokedTelegramHandler). Для group/supergroup требование без изменений.
        if (!isChannel && !perms.CanRestrictMembers) missing.Add("can_restrict_members");
        if (missing.Count > 0)
            return TelegramBotErrors.BotMissingRights(string.Join(", ", missing));

        // Reverse-claim (MembershipGrantsEnrollment) работает и для каналов (#416):
        // membership проверяется on-demand через getChatMember(channelId, userId) при условии,
        // что бот админ канала — это надёжно для закрытых каналов. Бот публикует закреплённое
        // claim-объявление с deep-link кнопкой (нужно can_post_messages; пин — best-effort).
        // EnforceMembership остаётся неприменимым для каналов: нет ChatMember updates для обычных
        // subscribers, поэтому реактивно кикать «не-членов» нельзя — клампим в false.
        bool membershipGrantsEnrollment = command.Request.MembershipGrantsEnrollment;
        bool enforceMembership = !isChannel && command.Request.EnforceMembership;

        // #687: курсовые ГРУППЫ авто-кикают на revoke/expire по умолчанию (надёжное удаление
        // истёкших участников). Bind-time дефолт владеет домен (ChatBinding.DefaultAutoKickOnRevoke):
        // для SUPERGROUP — true независимо от запроса (UI/MCP часто шлют false); для КАНАЛА —
        // значение запроса (kick на канале не поддерживается). Override после bind'а — через
        // PATCH флагов (UpdateChatBindingFlags).
        bool autoKickOnRevoke = isChannel
            ? command.Request.AutoKickOnRevoke
            : ChatBinding.DefaultAutoKickOnRevoke(chatType.Value);

        UnitResult<Error> beginResult = await _transactions.BeginTransactionAsync(cancellationToken);
        if (beginResult.IsFailure)
            return beginResult.Error;

        await _bindings.AcquirePlanMutationLockAsync(command.PlanId, cancellationToken);

        // Revalidate under the same plan lock used by PlanHardDeletedTelegramHandler.
        // The first lookup is an early fail-fast check; without this second lookup a bind
        // validated just before plan deletion could wait for the delete handler and then
        // insert an orphan binding after it released the lock.
        planResult = await _accessClient.GetPlanTelegramInfoAsync(command.PlanId, cancellationToken);
        if (planResult.IsFailure)
        {
            UnitResult<Error> releaseResult = await _transactions.CommitTransactionAsync(cancellationToken);
            return releaseResult.IsFailure ? releaseResult.Error : planResult.Error;
        }
        if (planResult.Value.CanonicalTelegramPlanId != command.PlanId)
        {
            UnitResult<Error> releaseResult = await _transactions.CommitTransactionAsync(cancellationToken);
            return releaseResult.IsFailure
                ? releaseResult.Error
                : CanonicalPlanRequired(planResult.Value.CanonicalTelegramPlanId);
        }
        if (command.Request.EnrollmentGrantsMembership && !HasCommunityAccess(planResult.Value))
        {
            UnitResult<Error> releaseResult = await _transactions.CommitTransactionAsync(cancellationToken);
            return releaseResult.IsFailure ? releaseResult.Error : CommunityAccessRequired();
        }

        // Идемпотентность: повторный bind того же чата к тому же плану — обновляем флаги + title.
        Guid planId = command.PlanId;
        long telegramChatId = chat.Id;
        Result<ChatBinding, Error> existing = await _bindings.GetByAsync(
            x => x.PlanId == planId && x.TelegramChatId == telegramChatId,
            cancellationToken);

        if (existing.IsSuccess)
        {
            existing.Value.UpdateFlags(
                command.Request.EnrollmentGrantsMembership,
                membershipGrantsEnrollment,
                autoKickOnRevoke,
                enforceMembership);
            existing.Value.UpdateChatMetadata(chat.Title, existing.Value.InviteLink);

            // A (#410): если объявление ещё не постили (напр. binding из до-фичевых времён) —
            // постим закреплённую claim-кнопку. Идемпотентно, best-effort.
            await _announcer.TryPostAndMarkAsync(existing.Value, cancellationToken);

            UnitResult<Error> updateSave = await _transactions.CommitTransactionAsync(cancellationToken);
            if (updateSave.IsFailure)
                return updateSave.Error;

            _logger.LogInformation(
                "Chat binding refreshed. BindingId={BindingId} PlanId={PlanId} ChatId={ChatId}",
                existing.Value.Id, planId, telegramChatId);

            return ToDto(existing.Value);
        }

        // Создаём invite link с creates_join_request=true.
        string linkName = $"plan_{command.PlanId.ToString("N", CultureInfo.InvariantCulture)[..8]}";
        ChatApiResult<string> linkResult = await _chatApi.CreateJoinRequestInviteLinkAsync(
            chat.Id, linkName, cancellationToken);
        if (linkResult.IsFailure)
            return TelegramBotErrors.ChatNotReachable();

        Result<ChatBinding, Error> createResult = ChatBinding.Create(
            id: Guid.CreateVersion7(),
            planId: command.PlanId,
            telegramChatId: chat.Id,
            chatType: chatType.Value,
            chatTitle: chat.Title,
            inviteLink: linkResult.Value!,
            enrollmentGrantsMembership: command.Request.EnrollmentGrantsMembership,
            membershipGrantsEnrollment: membershipGrantsEnrollment,
            autoKickOnRevoke: autoKickOnRevoke,
            enforceMembership: enforceMembership,
            createdBy: _user.UserId);
        if (createResult.IsFailure)
            return createResult.Error;

        await _bindings.AddAsync(createResult.Value, cancellationToken);

        // A (#410/#416): постим закреплённое claim-объявление в чат/канал, чтобы уже состоящие
        // участники / подписчики узнали, что могут забрать доступ. Best-effort; мутирует binding
        // (AnnouncementMessageId), сохраняется ниже одним SaveChangesAsync.
        await _announcer.TryPostAndMarkAsync(createResult.Value, cancellationToken);

        // Publish для AccessService onboarding-flow auto-sync (issue #68): когда первый
        // chat-binding появляется у плана, AccessService ensure'ит TELEGRAM step.
        await _outbox.PublishAsync(new ChatBindingBoundToPlan(
            createResult.Value.Id, command.PlanId, chat.Id));

        UnitResult<Error> saveResult = await _transactions.CommitTransactionAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Chat binding created. BindingId={BindingId} PlanId={PlanId} ChatId={ChatId} Type={Type} CreatedBy={CreatedBy}",
            createResult.Value.Id, command.PlanId, chat.Id, chatType, _user.UserId);

        return ToDto(createResult.Value);
    }

    private static bool HasCommunityAccess(PlanTelegramInfoDto plan) =>
        plan.Capabilities?.Contains(
            TelegramGrantChatAccessResolver.COMMUNITY_ACCESS,
            StringComparer.Ordinal) == true;

    private static Error CommunityAccessRequired() =>
        Error.Validation(
            "telegram.chat.plan.community_access_required",
            "План не предоставляет доступ к Telegram-сообществу");

    private static Error CanonicalPlanRequired(Guid? canonicalPlanId) =>
        Error.Validation(
            "telegram.chat.plan.canonical_required",
            canonicalPlanId is null
                ? "Для плана не настроен канонический Telegram-план"
                : $"Привяжите чат к каноническому плану {canonicalPlanId.Value.ToString("D", CultureInfo.InvariantCulture)}");

    private Task<ChatApiResult<ChatInfo>> ResolveChatAsync(string identifier, CancellationToken ct)
    {
        string trimmed = identifier.Trim();

        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long chatId))
            return _chatApi.GetChatAsync(chatId, ct);

        return _chatApi.GetChatByUsernameAsync(trimmed, ct);
    }

    private static ChatBindingDto ToDto(ChatBinding b) =>
        new(
            b.Id,
            b.PlanId,
            b.TelegramChatId,
            b.ChatType.ToString(),
            b.ChatTitle,
            b.InviteLink,
            b.EnrollmentGrantsMembership,
            b.MembershipGrantsEnrollment,
            b.AutoKickOnRevoke,
            b.EnforceMembership,
            b.CreatedAt,
            b.AnnouncementMessageId);
}
