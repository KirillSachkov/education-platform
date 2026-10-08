namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Ответ <c>POST /telegram/admin/users/{userId}/plans/{planId}/welcome/resend/</c> —
///     support-action «переотправить приветствие плана». <see cref="Outcome"/> — стабильный код:
///     <c>Sent | AlreadySent | NoWelcomeConfigured | SendFailed | NotLinked | NoChatBound</c>.
/// </summary>
public sealed record ResendWelcomeResponse(string Outcome);
