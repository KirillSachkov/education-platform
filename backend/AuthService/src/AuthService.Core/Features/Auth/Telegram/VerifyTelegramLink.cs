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

public sealed record VerifyTelegramLinkCommand(VerifyTelegramLinkRequest Request) : ICommand;

public sealed class VerifyTelegramLinkCommandValidator : AbstractValidator<VerifyTelegramLinkCommand>
{
    public VerifyTelegramLinkCommandValidator()
    {
        RuleFor(x => x.Request.LinkToken)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("linkToken"));

        RuleFor(x => x.Request.TelegramUserId)
            .GreaterThan(0).WithError(GeneralErrors.ValueIsInvalid("telegramUserId"));
    }
}

public sealed class VerifyTelegramLinkEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/auth/telegram/verify", async Task<EndpointResult<VerifyTelegramLinkResponse>> (
                    [FromBody] VerifyTelegramLinkRequest request,
                    [FromServices] VerifyTelegramLinkHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new VerifyTelegramLinkCommand(request), ct))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
}

public sealed class VerifyTelegramLinkHandler
    : ICommandHandler<VerifyTelegramLinkResponse, VerifyTelegramLinkCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly ITelegramLinkTokenStore _tokenStore;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<VerifyTelegramLinkCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public VerifyTelegramLinkHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        ITelegramLinkTokenStore tokenStore,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        IValidator<VerifyTelegramLinkCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenStore = tokenStore;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<VerifyTelegramLinkResponse, Error>> Handle(
        VerifyTelegramLinkCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        VerifyTelegramLinkRequest request = command.Request;
        string providerKey = request.TelegramUserId.ToString(CultureInfo.InvariantCulture);

        // Read the token WITHOUT consuming it — it is burned only after a durable commit (below).
        // This keeps verify idempotent under the bot's HTTP retries and Telegram's sticky START
        // button, both of which re-send the same token. Consuming up-front (the old behaviour)
        // burned the token before the link committed, so any failure/retry locked the user out
        // with a self-inflicted "link expired" error (#612).
        Guid? userId = await _tokenStore.PeekAsync(request.LinkToken);
        if (userId is null)
        {
            // Token gone (expired, or already deleted after an earlier success). If this Telegram
            // account is already linked, treat the repeat as idempotent success — the common case
            // is a re-pressed START / retried verify after the link already completed.
            Account? alreadyLinked = await _userManager.FindByLoginAsync(
                TelegramProviderConstants.PROVIDER_NAME, providerKey);
            if (alreadyLinked is not null)
                return new VerifyTelegramLinkResponse(alreadyLinked.Id, alreadyLinked.UserName ?? string.Empty);

            return AuthErrors.TelegramLinkTokenExpired();
        }

        Account? user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        Account? existingOwner = await _userManager.FindByLoginAsync(
            TelegramProviderConstants.PROVIDER_NAME, providerKey);

        if (existingOwner is not null)
        {
            // Telegram bound to a different account — leave the token intact (it maps to a
            // different user and is harmless; TTL reaps it) and surface the conflict.
            if (existingOwner.Id != user.Id)
                return AuthErrors.TelegramAlreadyLinkedToOther();

            // Idempotent: Telegram is already linked to the same user — burn the token, return success.
            await _tokenStore.DeleteAsync(request.LinkToken);
            return new VerifyTelegramLinkResponse(user.Id, user.UserName ?? string.Empty);
        }

        string? displayValue = !string.IsNullOrWhiteSpace(request.TelegramUsername)
            ? request.TelegramUsername
            : user.UserName;

        IdentityResult addResult = await _userManager.AddLoginAsync(
            user,
            new UserLoginInfo(TelegramProviderConstants.PROVIDER_NAME, providerKey, displayValue));

        if (!addResult.Succeeded)
            return AuthErrors.TelegramLinkFailed();

        // AddLoginAsync updates the security stamp, invalidating the Identity cookie.
        await _signInManager.RefreshSignInAsync(user);

        await _outboxService.PublishAsync(
            new UserTelegramLinked(user.Id, request.TelegramUserId, request.TelegramUsername));

        // Explicit SaveChanges flushes the Wolverine outbox envelope. Without this the
        // PublishAsync above only buffers into the DbContext, and the envelope is dropped
        // when the request scope disposes — see docs/agents/wolverine-tests.md.
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Burn the token only now — after the link is durably committed. A transient failure before
        // this point leaves the token intact so the bot's retry / a re-pressed START still succeeds.
        await _tokenStore.DeleteAsync(request.LinkToken);

        _audit.LogTelegramLinked(user.Id, request.TelegramUserId);

        return new VerifyTelegramLinkResponse(user.Id, user.UserName ?? string.Empty);
    }
}
