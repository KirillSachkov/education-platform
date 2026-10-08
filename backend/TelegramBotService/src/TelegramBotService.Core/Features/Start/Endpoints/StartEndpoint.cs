using Microsoft.Extensions.Logging;
using TelegramBotFlow.Core.Constants;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.MainMenu.Screens;

namespace TelegramBotService.Core.Features.Start.Endpoints;

/// <summary>
///     <c>/start</c> без payload — сбрасывает сессию, обновляет stale TelegramUsername
///     если юзер сменил его в Telegram, и ведёт на корневой экран.
///     Deep-link <c>/start &lt;token&gt;</c> обрабатывает <see cref="UserLinks.LinkAccountEndpoint"/>.
/// </summary>
public sealed class StartEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapCommand(BotCommands.START, async (
            UpdateContext ctx,
            IUserLinkRepository userLinks,
            ILogger<StartEndpoint> logger) =>
        {
            // Refresh username — fire-and-forget по сути (UPDATE одной колонки), не валит /start
            // если упадёт. Только если link существует и значение изменилось — иначе no-op.
            string? currentUsername = ctx.Update.Message?.From?.Username;
            try
            {
                await userLinks.UpdateTelegramUsernameIfChangedAsync(
                    ctx.UserId, currentUsername, ctx.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Failed to refresh TelegramUsername for user {UserId} on /start", ctx.UserId);
            }

            ctx.Session?.Clear();
            return BotResults.NavigateToRoot<MainMenuScreen>();
        });
    }
}
