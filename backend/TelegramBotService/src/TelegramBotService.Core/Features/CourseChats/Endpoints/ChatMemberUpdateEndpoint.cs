using Telegram.Bot.Types.Enums;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotService.Core.Features.CourseChats.Handlers;

namespace TelegramBotService.Core.Features.CourseChats.Endpoints;

/// <summary>
///     Регистрирует обработчик Telegram <c>chat_member</c> update'ов — изменение членства
///     в group/supergroup (юзер вступил/вышел/был добавлен админом).
///     Бот должен быть админом чата, чтобы получать эти update'ы.
///
///     <c>my_chat_member</c> (изменение статуса самого бота) обрабатывается отдельно через
///     <c>MapChatMember</c> в <c>ChatMemberEndpoint</c>.
/// </summary>
public sealed class ChatMemberUpdateEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapUpdate(
            ctx => ctx.Update.Type == UpdateType.ChatMember,
            (UpdateContext ctx, ChatMemberUpdateHandler handler) =>
                handler.HandleAsync(ctx));
    }
}
