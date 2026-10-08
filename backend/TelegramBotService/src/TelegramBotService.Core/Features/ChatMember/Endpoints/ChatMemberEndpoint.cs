using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotService.Core.Features.ChatMember.Handlers;

namespace TelegramBotService.Core.Features.ChatMember.Endpoints;

/// <summary>
///     <c>my_chat_member</c> — изменение статуса самого бота в чате (added/promoted/kicked).
///     Логика в <see cref="MyChatMemberHandler"/> — постит chat_id в чат при promotion'е, чтобы
///     админ мог скопировать его для UI платформы (вместо лазания по логам).
/// </summary>
public sealed class ChatMemberEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapChatMember((UpdateContext ctx, MyChatMemberHandler handler) =>
            handler.HandleAsync(ctx));
    }
}
