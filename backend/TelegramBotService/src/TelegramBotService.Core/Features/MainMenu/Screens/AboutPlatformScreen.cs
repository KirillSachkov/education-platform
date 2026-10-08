using Microsoft.Extensions.Options;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Screens;
using TelegramBotService.Core.Common;
using TelegramBotService.Core.Options;

namespace TelegramBotService.Core.Features.MainMenu.Screens;

/// <summary>
///     «О платформе» — экран для воронки. Показывается из unlinked main menu, объясняет
///     юзеру что даёт привязка аккаунта и как зарегистрироваться. Linked-юзеру не нужен.
/// </summary>
public sealed class AboutPlatformScreen : IScreen
{
    private readonly TelegramNotificationOptions _options;

    public AboutPlatformScreen(IOptions<TelegramNotificationOptions> options)
    {
        _options = options.Value;
    }

    public ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
    {
        ScreenView view = new(
            "<b>📚 Что это за платформа?</b>\n\n" +
            "Образовательная площадка с курсами, заданиями и Telegram-чатами участников.\n\n" +
            "<b>🔥 Что ты получишь после привязки аккаунта:</b>\n\n" +
            "• 📬 <b>Уведомления в Telegram</b> — новые материалы, ревью заданий, объявления авторов\n" +
            "• 💬 <b>Авто-доступ в чаты планов</b> — получил план → пришёл invite, нажал — бот пустил\n" +
            "• 🔓 <b>Доступ к плану через чат</b> — если ты уже в чате плана, можешь получить доступ на платформе\n" +
            "• ⏰ <b>Live-уведомления о ревью</b> — преподаватель проверил задание → ты узнал моментально\n\n" +
            "<b>🚀 Как начать:</b>\n\n" +
            "1️⃣ Зарегистрируйся / войди на платформу\n" +
            "2️⃣ В <b>Настройках → Telegram</b> нажми «Привязать»\n" +
            "3️⃣ Перейди по ссылке обратно сюда — бот завершит привязку\n\n" +
            "После этого все возможности откроются прямо в этом чате.");

        string registerUrl = BuildAbsoluteUrl("/login");
        string platformUrl = BuildAbsoluteUrl("/");

        if (UrlGuard.IsPublic(registerUrl))
            view.UrlButton("🔑 Войти / Регистрация", registerUrl).Row();

        if (UrlGuard.IsPublic(platformUrl))
            view.UrlButton("🌐 Открыть платформу", platformUrl).Row();

        view.BackButton();
        return ValueTask.FromResult(view);
    }

    private string BuildAbsoluteUrl(string relative)
    {
        string baseUrl = (_options.FrontendBaseUrl ?? string.Empty).TrimEnd('/');
        return baseUrl.Length == 0 ? relative : $"{baseUrl}{relative}";
    }
}
