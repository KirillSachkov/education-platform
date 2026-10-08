namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Ответ <c>GET /telegram/me/onboarding-status?planId=...</c> — статус привязки
///     Telegram + список чатов конкретного плана с их join-URL'ами. Используется
///     в TG-шаге plan-onboarding wizard'а на frontend.
/// </summary>
public sealed record PlanOnboardingStatusResponse(
    bool IsLinked,
    string? TelegramUsername,
    IReadOnlyList<PlanChatDto> Chats);

/// <summary>
///     Один tg-чат, привязанный к плану. <see cref="JoinUrl"/> — invite link
///     (creates_join_request). UI показывает кнопку «Открыть чат».
/// </summary>
public sealed record PlanChatDto(
    string ChatId,
    string? Title,
    string? JoinUrl,
    string ChatType,
    bool EnrollmentGrantsMembership);
