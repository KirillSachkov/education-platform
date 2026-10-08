using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Screens;

namespace TelegramBotService.Core.Features.Notifications.Screens;

/// <summary>
///     Заглушка истории уведомлений. Полная история доступна на сайте (Phase 4+).
/// </summary>
public sealed class RecentNotificationsScreen : IScreen
{
    public ValueTask<ScreenView> RenderAsync(UpdateContext ctx)
        => ValueTask.FromResult(
            new ScreenView(
                    "История уведомлений доступна на сайте в разделе «Уведомления». " +
                    "В следующих обновлениях бота появится просмотр последних событий прямо здесь.")
                .BackButton());
}
