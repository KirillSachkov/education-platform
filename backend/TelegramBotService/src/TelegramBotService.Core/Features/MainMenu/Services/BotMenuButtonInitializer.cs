using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace TelegramBotService.Core.Features.MainMenu.Services;

/// <summary>
///     Сбрасывает «menu button» (кнопка слева от поля ввода в DM с ботом) на дефолтный
///     <see cref="MenuButtonCommands"/> — список slash-команд. Telegram Bot API поддерживает
///     для этой кнопки только два типа: <see cref="MenuButtonCommands"/> или
///     <see cref="MenuButtonWebApp"/> (Mini App внутри Telegram). Обычной URL-кнопки,
///     открывающей платформу в браузере, в menu button НЕТ — для перехода на платформу
///     используем inline UrlButton'ы внутри <see cref="MainMenuScreen"/>.
///
///     Initializer нужен потому, что Telegram запоминает menu button per-bot между рестартами;
///     если кто-то из прошлой версии выставил WebApp — без явного сброса так и остаётся.
/// </summary>
internal sealed class BotMenuButtonInitializer : IHostedService
{
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<BotMenuButtonInitializer> _logger;

    public BotMenuButtonInitializer(
        ITelegramBotClient bot,
        ILogger<BotMenuButtonInitializer> logger)
    {
        _bot = bot;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _bot.SetChatMenuButton(
                menuButton: new MenuButtonCommands(),
                cancellationToken: cancellationToken);

            _logger.LogInformation("Bot menu button set to commands (slash menu)");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Не валим запуск сервиса — polling важнее.
            _logger.LogWarning(ex, "Failed to reset chat menu button — continuing without it");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
