namespace TelegramBotService.Contracts.HttpCommunication;

public sealed class TelegramBotServiceOptions
{
    public const string SECTION_NAME = nameof(TelegramBotServiceOptions);

    public string Url { get; init; } = string.Empty;

    public int TimeoutSeconds { get; init; } = 7;
}
