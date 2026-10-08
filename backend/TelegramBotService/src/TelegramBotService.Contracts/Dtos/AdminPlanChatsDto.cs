namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Admin/support-view всех chat-binding'ов плана
///     (<c>GET /telegram/admin/plans/{planId}/chats/</c>). Пустой план → <see cref="Chats"/>=[].
/// </summary>
public sealed record AdminPlanChatsDto(IReadOnlyList<AdminPlanChatDto> Chats);

/// <summary>
///     Один chat-binding плана с точки зрения админа/support: id чата, тип, invite-link и
///     флаг «членство в этом чате даётся за enrollment» (<see cref="EnrollmentGrantsMembership"/>).
/// </summary>
public sealed record AdminPlanChatDto(
    long TelegramChatId,
    string? ChatTitle,
    string ChatType,
    string? InviteLink,
    bool EnrollmentGrantsMembership);
