using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Screens;

namespace TelegramBotService.Core.Features.Settings.Screens;

/// <summary>
///     Заглушка настроек уведомлений. Управление каналами доставки пока доступно на сайте (Phase 4+).
/// </summary>
public sealed class SettingsScreen : IScreen
{
    public ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
        => ValueTask.FromResult(
            new ScreenView(
                    "Настройки уведомлений доступны на сайте в разделе профиля. " +
                    "В следующих обновлениях бота здесь появится управление каналами.")
                .BackButton());
}
