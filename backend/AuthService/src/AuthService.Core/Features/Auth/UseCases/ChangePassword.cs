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

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record ChangePasswordCommand(ChangePasswordRequest Request) : ICommand;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.Request.CurrentPassword)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("currentPassword"));

        RuleFor(x => x.Request.NewPassword)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("newPassword"))
            .IsValidPassword("newPassword");
    }
}

public sealed class ChangePasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // Self-service endpoint — any authenticated user can change their own password.
        app.MapPost("/auth/password/change", async Task<EndpointResult<string>> (
                    [FromBody] ChangePasswordRequest request,
                    [FromServices] ChangePasswordHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ChangePasswordCommand(request), ct))
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

public sealed class ChangePasswordHandler : ICommandHandler<string, ChangePasswordCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly UserScopedData _user;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly IValidator<ChangePasswordCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public ChangePasswordHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        UserScopedData user,
        TokenRevocationService tokenRevocation,
        IValidator<ChangePasswordCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _user = user;
        _tokenRevocation = tokenRevocation;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        ChangePasswordCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return GeneralErrors.NotFound(_user.UserId);

        IdentityResult result = await _userManager.ChangePasswordAsync(
            user, command.Request.CurrentPassword, command.Request.NewPassword);
        if (!result.Succeeded)
        {
            // PasswordMismatch → keep the generic "invalid credentials" so we don't
            // leak which side was wrong. All other Identity errors are about the NEW
            // password (length / casing / special-char) and the user needs to know what
            // to fix; the FluentValidation IsValidPassword rule normally catches these
            // first but Identity has stricter checks in some configurations.
            IdentityError firstError = result.Errors.First();
            return string.Equals(firstError.Code, "PasswordMismatch", StringComparison.Ordinal)
                ? AuthErrors.InvalidCredentials()
                : AuthErrors.FromIdentityError(firstError);
        }

        await _signInManager.RefreshSignInAsync(user);
        await _tokenRevocation.RevokeAllUserTokensAsync(_user.UserId, cancellationToken);

        _audit.LogPasswordChanged(_user.UserId);

        return "password_changed";
    }
}
