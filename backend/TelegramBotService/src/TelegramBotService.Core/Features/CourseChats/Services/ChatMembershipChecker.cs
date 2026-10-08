using Microsoft.Extensions.Logging;
using TelegramBotFlow.Core.Messaging;

namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Tri-state снимок «состоит ли юзер в чате прямо сейчас?». <see cref="Unknown"/>
///     означает, что Telegram API не ответил (rate limit, transient 5xx, network) — caller
///     сам решает, как трактовать неизвестность (fail-open или fail-closed).
/// </summary>
public enum MembershipStatus
{
    Member = 0,
    NotMember = 1,
    Unknown = 2,
}

/// <summary>
///     Точечная обёртка над <see cref="IChatAdministrationApi.GetChatMemberAsync"/> с tri-state
///     результатом. Используется четырьмя путями:
///     1. F1 (<c>CourseEnrolledTelegramHandler</c>) — не слать invite-DM если confirmed Member.
///        Unknown → шлём DM (fail-open: дубль безвредно).
///     2. Resync (<c>TelegramInviteResyncService</c>) — то же.
///     3. F6 (<c>CourseEnrollmentRevokedTelegramHandler</c>) — пропустить kick **только** если
///        confirmed NotMember. Unknown → kick (fail-closed: Telegram игнорирует kick не-членов,
///        вызов идемпотентен; это безопаснее, чем оставить revoked-юзера в чате при API-сбое).
///     4. <c>GetMyChatsHandler</c> — в DTO <c>isMember=true</c> только при confirmed Member;
///        Unknown показывается как «Войти» (fail-open для UX).
/// </summary>
public sealed class ChatMembershipChecker
{
    private readonly IChatAdministrationApi _chatApi;
    private readonly ILogger<ChatMembershipChecker> _logger;

    public ChatMembershipChecker(
        IChatAdministrationApi chatApi,
        ILogger<ChatMembershipChecker> logger)
    {
        _chatApi = chatApi;
        _logger = logger;
    }

    public async Task<MembershipStatus> GetStatusAsync(
        long telegramChatId, long telegramUserId, CancellationToken ct)
    {
        ChatApiResult<ChatMemberInfo>? result =
            await _chatApi.GetChatMemberAsync(telegramChatId, telegramUserId, ct);

        if (result is null || result.IsFailure || result.Value is null)
        {
            _logger.LogDebug(
                "ChatMembershipChecker: getChatMember unavailable (chat={ChatId} user={UserId}); status=Unknown",
                telegramChatId, telegramUserId);
            return MembershipStatus.Unknown;
        }

        return result.Value.IsActiveMember ? MembershipStatus.Member : MembershipStatus.NotMember;
    }
}
