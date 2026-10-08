using AuthService.Contracts;
using Core.HttpCommunication;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace TelegramBotService.Contracts.HttpCommunication;

internal sealed class AuthTelegramClient : BaseHttpClient, IAuthTelegramClient
{
    private const string SERVICE_NAME = "AuthService";

    public AuthTelegramClient(
        HttpClient httpClient,
        ILogger<AuthTelegramClient> logger)
        : base(httpClient, logger, SERVICE_NAME)
    {
    }

    public Task<Result<VerifyTelegramLinkResponse, Error>> VerifyAsync(
        string linkToken,
        long telegramUserId,
        string? telegramUsername,
        CancellationToken cancellationToken)
    {
        VerifyTelegramLinkRequest request = new(linkToken, telegramUserId, telegramUsername);
        return PostAsync<VerifyTelegramLinkRequest, VerifyTelegramLinkResponse>(
            "/auth/telegram/verify",
            request,
            cancellationToken);
    }

    public async Task<UnitResult<Error>> UnlinkByTelegramIdAsync(
        long telegramUserId,
        CancellationToken cancellationToken)
    {
        UnlinkTelegramByTelegramIdRequest request = new(telegramUserId);
        Result<string, Error> result = await PostAsync<UnlinkTelegramByTelegramIdRequest, string>(
            "/internal/telegram/unlink-by-telegram-id",
            request,
            cancellationToken);

        return result.IsSuccess
            ? UnitResult.Success<Error>()
            : UnitResult.Failure(result.Error);
    }
}
