using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Screens;

namespace TelegramBotService.Core.Features.UserLinks.Screens;

/// <summary>
///     Confirm-step для <c>/unlink</c>: предупреждение + кнопка «Да, отвязать».
///     Нужно чтобы юзер случайно не выбил привязку через slash-меню — unlink через
///     сайт + новый деплинк всё ещё возможен, но это лишнее трение, особенно если
///     юзер тапнул мимо.
/// </summary>
public sealed class UnlinkConfirmScreen : IScreen
{
    public ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
    {
        ScreenView view = new ScreenView(
            "⚠️ <b>Точно отвязать аккаунт?</b>\n\n" +
            "После отвязки:\n" +
            "• перестанут приходить уведомления в Telegram\n" +
            "• бот больше не будет автоматически пускать в чаты планов\n" +
            "• доступ к платформе и активные enrollment'ы сохраняются\n\n" +
            "Снова привязать можно через настройки на сайте.")
            .Button<ConfirmUnlinkAction>("✅ Да, отвязать")
            .Row()
            .BackButton();

        return ValueTask.FromResult(view);
    }
}

/// <summary>
///     Callback из <see cref="UnlinkConfirmScreen"/> — фактический unlink.
/// </summary>
public struct ConfirmUnlinkAction : IBotAction;
