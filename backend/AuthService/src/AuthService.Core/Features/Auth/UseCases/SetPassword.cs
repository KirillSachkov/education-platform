using AuthService.Core.Services;
using AuthService.Core.Validation;
using AuthService.Domain;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Validation.AspNetCore;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record SetPasswordRequest(string Password);

public sealed record SetPasswordCommand(SetPasswordRequest Request) : ICommand;

public sealed class SetPasswordCommandValidator : AbstractValidator<SetPasswordCommand>
{
    public SetPasswordCommandValidator()
    {
        RuleFor(x => x.Request.Password)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("password"))
            .IsValidPassword();
    }
}

public sealed class SetPasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Self-service endpoint — any authenticated user without a password (OTP/GitHub) can set one.
        app.MapPost("/auth/password/set", async Task<EndpointResult<string>> (
                    [FromBody] SetPasswordRequest request,
                    [FromServices] SetPasswordHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new SetPasswordCommand(request), ct))
            .RequireAuthorization(policy =>
            {
                policy.AddAuthenticationSchemes(
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
                    IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
            })
            .RequireRateLimiting("login");
    }
}

public sealed class SetPasswordHandler : ICommandHandler<string, SetPasswordCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly UserScopedData _user;
    private readonly IValidator<SetPasswordCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public SetPasswordHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        TokenRevocationService tokenRevocation,
        UserScopedData user,
        IValidator<SetPasswordCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenRevocation = tokenRevocation;
        _user = user;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        SetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return GeneralErrors.NotFound(_user.UserId);

        if (await _userManager.HasPasswordAsync(user))
            return AuthErrors.PasswordAlreadySet();

        IdentityResult result = await _userManager.AddPasswordAsync(user, command.Request.Password);
        if (!result.Succeeded)
            return AuthErrors.FromIdentityError(result.Errors.First());

        await _tokenRevocation.RevokeAllUserTokensAsync(_user.UserId, cancellationToken);
        await _signInManager.RefreshSignInAsync(user);

        _audit.LogPasswordSet(_user.UserId);

        return "password_set";
    }
}
