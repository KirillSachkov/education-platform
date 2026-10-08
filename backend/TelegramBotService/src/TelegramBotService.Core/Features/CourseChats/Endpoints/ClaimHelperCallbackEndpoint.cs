using System.Globalization;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Core.Features.CourseChats.Screens;

namespace TelegramBotService.Core.Features.CourseChats.Endpoints;

/// <summary>
///     Inline-callback из <see cref="ClaimAccessHelperScreen"/>: формат
///     <c>helper:claim:&lt;chatId&gt;</c>. Делегирует на
///     <see cref="ClaimCourseAccessHandler.ClaimByChatIdAsync"/>.
/// </summary>
public sealed class ClaimHelperCallbackEndpoint : IBotEndpoint
{
    private const string CALLBACK_PREFIX = "helper:claim:";

    public void MapEndpoint(BotApplication app)
    {
        app.MapCallback($"{CALLBACK_PREFIX}*", (UpdateContext ctx, ClaimCourseAccessHandler handler) =>
        {
            string callbackData = ctx.CallbackData ?? string.Empty;
            string chatIdStr = callbackData.Length > CALLBACK_PREFIX.Length
                ? callbackData[CALLBACK_PREFIX.Length..]
                : string.Empty;

            if (!long.TryParse(chatIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long chatId))
                return Task.FromResult(BotResults.Empty());

            return handler.ClaimByChatIdAsync(ctx, chatId);
        });
    }
}
