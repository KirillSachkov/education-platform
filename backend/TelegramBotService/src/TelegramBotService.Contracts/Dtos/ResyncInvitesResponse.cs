namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Ответ <c>POST /telegram/me/resync-invites</c>: количество DM-инвайтов,
///     отправленных юзеру по результатам пере-проверки enrollment'ов и chat-bindings.
/// </summary>
public sealed record ResyncInvitesResponse(int InvitesSent, bool TelegramLinked);
