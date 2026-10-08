using TelegramBotFlow.Core.Constants;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotService.Core.Features.CourseChats.Handlers;
using TelegramBotService.Core.Features.UserLinks.Handlers;

namespace TelegramBotService.Core.Features.UserLinks.Endpoints;

/// <summary>
///     Диспетчер deep-link'ов <c>/start &lt;payload&gt;</c>:
///     — payload с префиксом <c>claim_chat_</c> → <see cref="ClaimCourseAccessHandler"/> (F3 reverse-флоу).
///     — иначе считаем payload link-token'ом и идём в <see cref="LinkAccountHandler"/> (привязка аккаунта).
/// </summary>
public sealed class LinkAccountEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapDeepLink(BotCommands.START, (
            UpdateContext ctx,
            LinkAccountHandler linker,
            ClaimCourseAccessHandler claimer) =>
        {
            string? payload = ctx.CommandArgument;
            if (payload is not null && payload.StartsWith(ClaimCourseAccessHandler.PAYLOAD_PREFIX, StringComparison.Ordinal))
                return claimer.HandleAsync(ctx);

            return linker.HandleAsync(ctx);
        });
    }
}
