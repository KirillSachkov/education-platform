using AuthService.Contracts;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;
using TelegramBotFlow.Core.Context;
using TelegramBotFlow.Core.Messaging;
using TelegramBotFlow.Core.Routing;
using TelegramBotService.Contracts.HttpCommunication;
using TelegramBotService.Core.Database;
using TelegramBotService.Core.Features.MainMenu.Screens;
using TelegramBotService.Domain;
using TelegramBotService.Domain.UserLinks;

namespace TelegramBotService.Core.Features.UserLinks.Handlers;

/// <summary>
///     Логика deep-link <c>/start &lt;token&gt;</c>: верифицирует токен через AuthService,
///     создаёт локальный <see cref="UserLink"/> и шлёт пользователю фидбек.
///     Идемпотентен: повторный deep-link для уже привязанного пользователя — no-op + greeting.
/// </summary>
public sealed class LinkAccountHandler
{
    private readonly IAuthTelegramClient _authClient;
    private readonly IUserLinkRepository _userLinks;
    private readonly ITransactionManager _transactions;
    private readonly IBotNotifier _notifier;
    private readonly ILogger<LinkAccountHandler> _logger;

    public LinkAccountHandler(
        IAuthTelegramClient authClient,
        IUserLinkRepository userLinks,
        ITransactionManager transactions,
        IBotNotifier notifier,
        ILogger<LinkAccountHandler> logger)
    {
        _authClient = authClient;
        _userLinks = userLinks;
        _transactions = transactions;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<IEndpointResult> HandleAsync(UpdateContext ctx)
    {
        string linkToken = ctx.CommandArgument!;
        long telegramUserId = ctx.UserId;
        string? telegramUsername = ctx.Update.Message?.From?.Username;

        Result<VerifyTelegramLinkResponse, Error> verifyResult = await _authClient.VerifyAsync(
            linkToken,
            telegramUserId,
            telegramUsername,
            ctx.CancellationToken);

        if (verifyResult.IsFailure)
        {
            return await VerifyFailedAsync(ctx, verifyResult.Error);
        }

        VerifyTelegramLinkResponse verified = verifyResult.Value;

        Result<UserLink, Error> linkResult = UserLink.Create(
            telegramUserId,
            verified.UserId,
            telegramUsername);

        if (linkResult.IsFailure)
        {
            return await LinkCreateFailedAsync(ctx, linkResult.Error);
        }

        UnitResult<Error> persisted = await EnsurePersistedAsync(linkResult.Value, telegramUserId, ctx.CancellationToken);
        if (persisted.IsFailure)
        {
            return await PersistFailedAsync(ctx, persisted.Error);
        }

        await SendSuccessAsync(ctx, verified.Username);

        ctx.Session?.Clear();
        return BotResults.NavigateToRoot<MainMenuScreen>();
    }

    private async Task<UnitResult<Error>> EnsurePersistedAsync(
        UserLink link,
        long telegramUserId,
        CancellationToken ct)
    {
        // GetBy возвращает tracked entity — если link уже существует, можем мутировать
        // (Unblock при необходимости) и сохранять через тот же DbContext.
        Result<UserLink, Error> existingResult = await _userLinks.GetBy(
            x => x.TelegramUserId == telegramUserId, ct);

        if (existingResult.IsSuccess)
        {
            UserLink existing = existingResult.Value;

            // Soft-block recovery: link был помечен Block'ом после bot_blocked /
            // chat_not_found, теперь юзер вернулся через /start — снимаем флаг.
            if (!existing.IsActive)
            {
                _logger.LogInformation(
                    "Unblocking soft-blocked Telegram link for user {UserId} (was {Reason})",
                    existing.PlatformUserId, existing.BlockedReason);
                existing.Unblock();
                return await _transactions.SaveChangesAsync(ct);
            }

            return UnitResult.Success<Error>();
        }

        await _userLinks.AddAsync(link, ct);

        UnitResult<Error> saveResult = await _transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure
            && !string.Equals(
                saveResult.Error.Messages[0].Code,
                TelegramBotErrors.USER_LINK_ALREADY_EXISTS_CODE,
                StringComparison.Ordinal))
        {
            return saveResult;
        }

        return UnitResult.Success<Error>();
    }

    private async Task<IEndpointResult> VerifyFailedAsync(UpdateContext ctx, Error error)
    {
        ErrorMessage firstError = error.Messages[0];
        _logger.LogInformation(
            "Telegram link verify failed for chat {ChatId}: {Code} {Message}",
            ctx.ChatId,
            firstError.Code,
            firstError.Message);

        await _notifier.SendTextAsync(
            ctx.ChatId,
            $"Не удалось привязать аккаунт: {firstError.Message}. " +
            "Получите новую ссылку на сайте и попробуйте снова.",
            ct: ctx.CancellationToken);

        return BotResults.Empty();
    }

    private async Task<IEndpointResult> LinkCreateFailedAsync(UpdateContext ctx, Error error)
    {
        _logger.LogWarning(
            "UserLink.Create failed for chat {ChatId}: {Code}",
            ctx.ChatId,
            error.Messages[0].Code);

        await NotifyPersistFailureAsync(ctx);
        return BotResults.Empty();
    }

    private async Task<IEndpointResult> PersistFailedAsync(UpdateContext ctx, Error error)
    {
        _logger.LogWarning(
            "Failed to persist UserLink for chat {ChatId}: {Code}",
            ctx.ChatId,
            error.Messages[0].Code);

        await NotifyPersistFailureAsync(ctx);
        return BotResults.Empty();
    }

    private Task NotifyPersistFailureAsync(UpdateContext ctx) =>
        _notifier.SendTextAsync(
            ctx.ChatId,
            "Не удалось сохранить связь с аккаунтом. Попробуйте позже.",
            ct: ctx.CancellationToken);

    private async Task SendSuccessAsync(UpdateContext ctx, string? verifiedUsername)
    {
        string greetingUsername = !string.IsNullOrWhiteSpace(verifiedUsername)
            ? verifiedUsername
            : "пользователь";

        await _notifier.SendTextAsync(
            ctx.ChatId,
            $"✅ Аккаунт {greetingUsername} успешно связан. " +
            "Теперь вы будете получать уведомления здесь.",
            ct: ctx.CancellationToken);
    }
}
