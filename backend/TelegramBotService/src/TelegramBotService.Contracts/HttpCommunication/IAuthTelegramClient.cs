using AuthService.Contracts;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace TelegramBotService.Contracts.HttpCommunication;

/// <summary>
///     Service-to-service HTTP-клиент к AuthService для верификации Telegram link-token'ов.
///     Service-to-service HTTP client to AuthService used to verify Telegram link tokens.
/// </summary>
public interface IAuthTelegramClient
{
    /// <summary>
    ///     Вызывает <c>POST /auth/telegram/verify</c> на AuthService.
    ///     Calls <c>POST /auth/telegram/verify</c> on AuthService.
    /// </summary>
    Task<Result<VerifyTelegramLinkResponse, Error>> VerifyAsync(
        string linkToken,
        long telegramUserId,
        string? telegramUsername,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Вызывает <c>POST /internal/telegram/unlink-by-telegram-id</c> — удаляет OIDC-привязку
    ///     Telegram-аккаунта в <c>auth.user_logins</c>. Идемпотентен: если привязки нет — success.
    ///     Используется bot-side <c>/unlink</c>, чтобы не оставлять осиротевший UserLogin row,
    ///     который блокировал бы повторную привязку из-под другого платформенного пользователя.
    /// </summary>
    Task<UnitResult<Error>> UnlinkByTelegramIdAsync(
        long telegramUserId,
        CancellationToken cancellationToken);
}
