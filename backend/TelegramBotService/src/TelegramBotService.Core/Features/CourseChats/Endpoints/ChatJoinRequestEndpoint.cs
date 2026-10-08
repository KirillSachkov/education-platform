using Telegram.Bot.Types.Enums;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotService.Core.Features.CourseChats.Handlers;

namespace TelegramBotService.Core.Features.CourseChats.Endpoints;

/// <summary>
///     Регистрирует обработчик Telegram <c>chat_join_request</c> update'а — юзер кликнул
///     invite link с <c>creates_join_request=true</c>. Логика в <see cref="ChatJoinRequestHandler"/>.
/// </summary>
public sealed class ChatJoinRequestEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapUpdate(
            ctx => ctx.Update.Type == UpdateType.ChatJoinRequest,
            (UpdateContext ctx, ChatJoinRequestHandler handler) =>
                handler.HandleAsync(ctx));
    }
}
