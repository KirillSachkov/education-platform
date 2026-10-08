using System.Globalization;
using AuthService.Contracts;
using AuthService.Core.Database;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Auth.Telegram;

public sealed record UnlinkTelegramByTelegramIdCommand(
    UnlinkTelegramByTelegramIdRequest Request) : ICommand;

/// <summary>
///     Internal service-to-service endpoint: removes the Telegram external login by its
///     ProviderKey (telegramUserId) regardless of which platform user owns it. Used by
///     TelegramBotService's <c>/unlink</c> handler so a bot-side unlink fully detaches the
///     OIDC login row in <c>auth.user_logins</c> — without it, a stale row blocks every
///     subsequent attempt by another platform user to link the same Telegram account.
///
///     Idempotent: returns success if no Telegram login is found for the given id.
/// </summary>
public sealed class UnlinkTelegramByTelegramIdEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/internal/telegram/unlink-by-telegram-id",
                async Task<EndpointResult<string>> (
                    [FromBody] UnlinkTelegramByTelegramIdRequest request,
                    [FromServices] UnlinkTelegramByTelegramIdHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UnlinkTelegramByTelegramIdCommand(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class UnlinkTelegramByTelegramIdCommandValidator
    : AbstractValidator<UnlinkTelegramByTelegramIdCommand>
{
    public UnlinkTelegramByTelegramIdCommandValidator()
    {
        RuleFor(x => x.Request.TelegramUserId)
            .GreaterThan(0).WithError(GeneralErrors.ValueIsInvalid("telegramUserId"));
    }
}

public sealed class UnlinkTelegramByTelegramIdHandler
    : ICommandHandler<string, UnlinkTelegramByTelegramIdCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<UnlinkTelegramByTelegramIdCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public UnlinkTelegramByTelegramIdHandler(
        UserManager<Account> userManager,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        IValidator<UnlinkTelegramByTelegramIdCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        UnlinkTelegramByTelegramIdCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string providerKey = command.Request.TelegramUserId
            .ToString(CultureInfo.InvariantCulture);

        Account? user = await _userManager.FindByLoginAsync(
            TelegramProviderConstants.PROVIDER_NAME, providerKey);

        if (user is null)
            return "telegram_unlinked";

        IdentityResult result = await _userManager.RemoveLoginAsync(
            user, TelegramProviderConstants.PROVIDER_NAME, providerKey);

        if (!result.Succeeded)
        {
            _audit.LogTelegramUnlinkFailed(
                user.Id,
                string.Join(", ", result.Errors.Select(e => e.Description)));
            return AuthErrors.TelegramUnlinkFailed();
        }

        await _outboxService.PublishAsync(
            new UserTelegramUnlinked(user.Id, command.Request.TelegramUserId));

        // Explicit SaveChanges flushes the Wolverine outbox envelope. Service-to-service
        // caller, so no cookie/security-stamp refresh is needed here (unlike UnlinkTelegram).
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _audit.LogTelegramUnlinked(user.Id, command.Request.TelegramUserId);

        return "telegram_unlinked";
    }
}
