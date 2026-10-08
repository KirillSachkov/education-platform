using System.Globalization;
using AuthService.Core.Database;
using AuthService.Core.Features.Auth.Telegram;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Core.Database;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Validation.AspNetCore;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Users.UseCases;

public sealed record UnlinkTelegramCommand : ICommand;

public sealed class UnlinkTelegramEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/me/telegram/unlink", async Task<EndpointResult<string>> (
                    [Microsoft.AspNetCore.Mvc.FromServices] UnlinkTelegramHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new UnlinkTelegramCommand(), ct))
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                    IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
            });
}

public sealed class UnlinkTelegramHandler : ICommandHandler<string, UnlinkTelegramCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ILogger<AuthAudit> _audit;

    public UnlinkTelegramHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        UserScopedData user,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        UnlinkTelegramCommand command,
        CancellationToken cancellationToken)
    {
        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        IList<UserLoginInfo> logins = await _userManager.GetLoginsAsync(user);
        UserLoginInfo? telegramLogin = logins.FirstOrDefault(l => string.Equals(
            l.LoginProvider, TelegramProviderConstants.PROVIDER_NAME, StringComparison.Ordinal));

        if (telegramLogin is null)
            return "telegram_unlinked";

        IdentityResult removeResult = await _userManager.RemoveLoginAsync(
            user, telegramLogin.LoginProvider, telegramLogin.ProviderKey);

        if (!removeResult.Succeeded)
        {
            _audit.LogTelegramUnlinkFailed(
                _user.UserId,
                string.Join(", ", removeResult.Errors.Select(e => e.Description)));
            return AuthErrors.TelegramUnlinkFailed();
        }

        // Re-issue Identity cookie with updated security stamp.
        await _signInManager.RefreshSignInAsync(user);

        if (!long.TryParse(telegramLogin.ProviderKey, NumberStyles.Integer, CultureInfo.InvariantCulture, out long telegramUserId))
            telegramUserId = 0;

        await _outboxService.PublishAsync(
            new UserTelegramUnlinked(_user.UserId, telegramUserId));

        // Explicit SaveChanges flushes the Wolverine outbox envelope — without this the
        // integration event would be dropped on scope dispose (see wolverine-tests.md).
        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _audit.LogTelegramUnlinked(_user.UserId, telegramUserId);

        return "telegram_unlinked";
    }
}
