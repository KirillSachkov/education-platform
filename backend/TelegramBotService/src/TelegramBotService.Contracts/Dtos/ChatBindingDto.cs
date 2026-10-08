namespace TelegramBotService.Contracts.Dtos;

public sealed record ChatBindingDto(
    Guid Id,
    Guid PlanId,
    long TelegramChatId,
    string ChatType,
    string? ChatTitle,
    string InviteLink,
    bool EnrollmentGrantsMembership,
    bool MembershipGrantsEnrollment,
    bool AutoKickOnRevoke,
    bool EnforceMembership,
    DateTime CreatedAt,
    // message_id закреплённого claim-объявления (#410/#416). NULL = ещё не опубликовано
    // (или не удалось — напр. бот не админ канала / нет can_post_messages). UI показывает статус.
    int? AnnouncementMessageId = null);
