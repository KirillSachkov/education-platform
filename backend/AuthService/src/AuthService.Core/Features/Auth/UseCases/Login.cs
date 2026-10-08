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
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginCommand(LoginRequest Request) : ICommand;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));

        RuleFor(x => x.Request.Password)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("password"));
    }
}

public sealed class LoginEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async Task<EndpointResult<string>> (
                    [FromBody] LoginRequest request,
                    [FromServices] LoginHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new LoginCommand(request), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("login");
    }
}

public sealed class LoginHandler : ICommandHandler<string, LoginCommand>
{
    private readonly SignInManager<Account> _signInManager;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<LoginCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public LoginHandler(
        SignInManager<Account> signInManager,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        IValidator<LoginCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _signInManager = signInManager;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        Account? user = await _signInManager.UserManager.FindByEmailAsync(email);
        if (user is null)
        {
            _audit.LogLoginFailed(email, "invalid_credentials");
            return AuthErrors.InvalidCredentials();
        }

        SignInResult result = await _signInManager.PasswordSignInAsync(
            user,
            command.Request.Password,
            isPersistent: true,
            lockoutOnFailure: true);

        if (result.IsNotAllowed)
        {
            _audit.LogLoginFailed(email, "email_not_confirmed");
            return AuthErrors.EmailNotConfirmed();
        }

        if (result.IsLockedOut)
        {
            _audit.LogLoginFailed(email, "locked");
            return AuthErrors.AccountLocked();
        }

        if (!result.Succeeded)
        {
            _audit.LogLoginFailed(email, "invalid_credentials");
            return AuthErrors.InvalidCredentials();
        }

        // Update last_login_at for admin observability. Tracked entity — SaveChanges flushes it.
        user.LastLoginAt = DateTime.UtcNow;

        // Publish UserLoggedIn — login-audit / session-lifecycle event для consumers.
        await _outboxService.PublishAsync(new UserLoggedIn(user.Id, user.UserName));
        UnitResult<Error> publishResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (publishResult.IsFailure)
            return publishResult.Error;

        _audit.LogLoginSuccess(user.Id, "password");

        return "authenticated";
    }
}
