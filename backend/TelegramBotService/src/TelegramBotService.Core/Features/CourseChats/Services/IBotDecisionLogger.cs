namespace TelegramBotService.Core.Features.CourseChats.Services;

/// <summary>
///     Interface для аудит-логгера решений бота. Прод-реализация — <see cref="BotDecisionLogger"/>;
///     тесты подменяют через <c>Substitute.For&lt;IBotDecisionLogger&gt;()</c> (audit-write
///     не относится к business-logic тестируемых handler'ов).
/// </summary>
public interface IBotDecisionLogger
{
    Task LogAsync(
        long telegramChatId,
        long telegramUserId,
        string decision,
        string? reason = null,
        Guid? planId = null,
        CancellationToken ct = default);
}
