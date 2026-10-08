using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;

namespace TelegramBotService.Core.Features.Help.Endpoints;

/// <summary>
///     <c>/help</c> — справка по возможностям бота. Список команд и краткое описание
///     основных флоу. Без интерактива (всю навигацию даёт <c>/start</c>).
/// </summary>
public sealed class HelpEndpoint : IBotEndpoint
{
    private const string HELP_TEXT =
        "🤖 <b>Бот образовательной платформы</b>\n\n" +
        "<b>Команды:</b>\n" +
        "• /start — главное меню\n" +
        "• /unlink — отключить уведомления (с подтверждением)\n" +
        "• /help — эта справка\n\n" +
        "<b>Что я умею:</b>\n" +
        "📬 Слать тебе уведомления о новых уроках, проверках работ и комментариях.\n" +
        "💬 Автоматически пускать в Telegram-чаты планов, на которые у тебя есть доступ.\n" +
        "🔓 Если ты УЖЕ в чате плана — попроси меня выдать доступ к нему на платформе через меню «Получить доступ из чата».\n\n" +
        "<b>Каналы и настройки</b> — на платформе в разделе «Настройки → Уведомления». " +
        "Там же можно отключить отдельные типы уведомлений.\n\n" +
        "Если что-то не работает — напиши /start чтобы перезагрузить меню, или зайди на платформу.";

    public void MapEndpoint(BotApplication app)
    {
        app.MapCommand("/help", async (UpdateContext ctx, IBotNotifier notifier) =>
        {
            await notifier.SendTextAsync(ctx.ChatId, HELP_TEXT, ct: ctx.CancellationToken);
            return BotResults.Empty();
        });
    }
}
