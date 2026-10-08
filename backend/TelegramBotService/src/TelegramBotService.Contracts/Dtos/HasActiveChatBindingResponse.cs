namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     S2S response от <c>GET /internal/telegram/plans/{planId}/has-active-chat-binding/</c>.
/// </summary>
public sealed record HasActiveChatBindingResponse(bool HasActive);
