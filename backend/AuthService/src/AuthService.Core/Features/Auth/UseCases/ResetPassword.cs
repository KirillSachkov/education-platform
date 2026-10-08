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
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ResetPasswordCommand(ResetPasswordRequest Request) : ICommand;

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));

        RuleFor(x => x.Request.Token)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("token"));

        RuleFor(x => x.Request.NewPassword)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("newPassword"))
            .IsValidPassword("newPassword");
    }
}

public sealed class ResetPasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/password/reset", async Task<EndpointResult<string>> (
                    [FromBody] ResetPasswordRequest request,
                    [FromServices] ResetPasswordHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ResetPasswordCommand(request), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("password-reset");
    }
}

public sealed class ResetPasswordHandler : ICommandHandler<string, ResetPasswordCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly IValidator<ResetPasswordCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public ResetPasswordHandler(
        UserManager<Account> userManager,
        TokenRevocationService tokenRevocation,
        IValidator<ResetPasswordCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _tokenRevocation = tokenRevocation;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        Account? user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return AuthErrors.InvalidResetToken();

        IdentityResult result = await _userManager.ResetPasswordAsync(
            user, command.Request.Token, command.Request.NewPassword);
        if (!result.Succeeded)
            return AuthErrors.InvalidResetToken();

        await _tokenRevocation.RevokeAllUserTokensAsync(user.Id, cancellationToken);

        _audit.LogPasswordReset(user.Id);

        return "password_reset";
    }
}
