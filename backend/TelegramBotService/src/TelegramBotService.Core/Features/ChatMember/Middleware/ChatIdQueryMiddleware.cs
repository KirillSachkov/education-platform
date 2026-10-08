using System.Globalization;
using Telegram.Bot.Types.Enums;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Pipeline;

namespace TelegramBotService.Core.Features.ChatMember.Middleware;

/// <summary>
///     Перехватывает <c>/chatid</c> в group/supergroup/channel <strong>до</strong>
///     <c>UsePrivateChatOnly</c> — отвечает chat_id'ом и не пропускает дальше.
///     Нужно админу при ручной привязке Telegram-чата к курсу через UI платформы.
///
///     В личке команда не работает — там это бессмысленно (chat_id == user_id, и так видно).
/// </summary>
public sealed class ChatIdQueryMiddleware : IUpdateMiddleware
{
    private const string COMMAND = "/chatid";

    private readonly IBotNotifier _notifier;

    public ChatIdQueryMiddleware(IBotNotifier notifier) => _notifier = notifier;

    public async Task InvokeAsync(UpdateContext context, UpdateDelegate next)
    {
        if (TryExtract(context, out long chatId, out string? title))
        {
            string text = $"Chat ID: <code>{chatId.ToString(CultureInfo.InvariantCulture)}</code>"
                + (string.IsNullOrEmpty(title) ? string.Empty : $"\nName: {title}")
                + "\n\nСкопируй и используй на платформе при привязке к плану.";

            await _notifier.SendTextAsync(
                chatId, text, parseMode: ParseMode.Html, ct: context.CancellationToken);
            return;
        }

        await next(context);
    }

    private static bool TryExtract(UpdateContext context, out long chatId, out string? title)
    {
        chatId = 0;
        title = null;

        if (context.Update.Type == UpdateType.Message
            && string.Equals(context.Update.Message?.Text?.Trim(), COMMAND, StringComparison.Ordinal)
            && context.Update.Message?.Chat is { } messageChat
            && messageChat.Type is Telegram.Bot.Types.Enums.ChatType.Group
                                or Telegram.Bot.Types.Enums.ChatType.Supergroup)
        {
            chatId = messageChat.Id;
            title = messageChat.Title;
            return true;
        }

        if (context.Update.Type == UpdateType.ChannelPost
            && string.Equals(context.Update.ChannelPost?.Text?.Trim(), COMMAND, StringComparison.Ordinal)
            && context.Update.ChannelPost?.Chat is { } channelChat)
        {
            chatId = channelChat.Id;
            title = channelChat.Title;
            return true;
        }

        return false;
    }
}
