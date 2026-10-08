namespace AuthService.Core.Features.Auth.Telegram;

/// <summary>
///     Константы для провайдера Telegram в Identity <c>UserLoginInfo</c> и endpoint-маршрутов.
/// </summary>
public static class TelegramProviderConstants
{
    /// <summary>
    ///     Имя провайдера в <c>user_logins</c> (Identity). Менять нельзя — нарушит обратную совместимость.
    /// </summary>
    public const string PROVIDER_NAME = "Telegram";
}
