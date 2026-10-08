namespace TelegramBotService.Contracts.Requests;

/// <summary>
///     Привязать Telegram-чат к курсу.
///     <c>ChatIdentifier</c> — либо numeric chat_id (вкл. отрицательные для групп),
///     либо <c>@username</c> публичной группы/канала.
/// </summary>
public sealed record BindChatRequest(
    string ChatIdentifier,
    bool EnrollmentGrantsMembership = true,
    bool MembershipGrantsEnrollment = true,
    bool AutoKickOnRevoke = false,
    bool EnforceMembership = false);

public sealed record UpdateChatBindingFlagsRequest(
    bool EnrollmentGrantsMembership,
    bool MembershipGrantsEnrollment,
    bool AutoKickOnRevoke,
    bool EnforceMembership);
