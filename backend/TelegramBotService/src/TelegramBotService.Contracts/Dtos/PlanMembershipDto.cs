namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     S2S response от
///     <c>GET /internal/telegram/users/{userId}/plans/{planId}/membership</c>.
///     <see cref="IsMember"/> = юзер состоит хотя бы в одном из bound чатов плана.
///     <see cref="Status"/> ∈ <c>"member" | "not_member" | "unknown"</c>
///     (<c>unknown</c> — нет UserLink или Telegram API не ответил).
/// </summary>
public sealed record PlanMembershipDto(bool IsMember, string Status);
