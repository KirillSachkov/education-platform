using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Endpoints;
using TelegramBotFlow.Core.Hosting;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Core.Features.UserLinks.Handlers;
using TelegramBotService.Core.Features.UserLinks.Screens;

namespace TelegramBotService.Core.Features.UserLinks.Endpoints;

/// <summary>
///     <c>/unlink</c> — двухшаговый flow: команда показывает <see cref="UnlinkConfirmScreen"/>
///     с предупреждением; фактический unlink выполняется только после нажатия кнопки
///     «✅ Да, отвязать» (callback <see cref="ConfirmUnlinkAction"/>) → <see cref="UnlinkHandler"/>.
/// </summary>
public sealed class UnlinkEndpoint : IBotEndpoint
{
    public void MapEndpoint(BotApplication app)
    {
        app.MapCommand("/unlink", (UpdateContext _) =>
            Task.FromResult(BotResults.NavigateTo<UnlinkConfirmScreen>()));

        app.MapAction<ConfirmUnlinkAction>((UpdateContext ctx, UnlinkHandler handler) =>
            handler.HandleAsync(ctx));
    }
}
