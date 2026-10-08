using System.Web;
using AuthService.Core.Options;
using AuthService.Core.Services;
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
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record ForgotPasswordRequest(string Email);

public sealed record ForgotPasswordCommand(ForgotPasswordRequest Request) : ICommand;

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));
    }
}

public sealed class ForgotPasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/password/forgot", async Task<EndpointResult<string>> (
                    [FromBody] ForgotPasswordRequest request,
                    [FromServices] ForgotPasswordHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new ForgotPasswordCommand(request), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("password-reset");
    }
}

public sealed class ForgotPasswordHandler : ICommandHandler<string, ForgotPasswordCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly IAuthEmailSender _emailSender;
    private readonly AuthServiceOptions _authOptions;
    private readonly IValidator<ForgotPasswordCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public ForgotPasswordHandler(
        UserManager<Account> userManager,
        IAuthEmailSender emailSender,
        IOptions<AuthServiceOptions> authOptions,
        IValidator<ForgotPasswordCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _authOptions = authOptions.Value;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        Account? user = await _userManager.FindByEmailAsync(email);

        // Always return success to prevent email enumeration
        if (user is null)
            return "reset_email_sent";

        string token = await _userManager.GeneratePasswordResetTokenAsync(user);
        string resetUrl = $"{_authOptions.FrontendBaseUrl}/login/reset-password" +
                          $"?email={HttpUtility.UrlEncode(email)}&token={HttpUtility.UrlEncode(token)}";

        bool sent = await _emailSender.SendPasswordResetAsync(email, resetUrl, cancellationToken);
        if (!sent)
        {
            // Do not surface the failure to the caller — that would allow email enumeration
            // by probing which addresses trigger delivery errors. Log for ops visibility instead.
            // Log the target user id (not the email) so we don't persist PII in logs.
            _audit.LogWarning("Password reset email delivery failed for user {UserId}", user.Id);
        }

        _audit.LogPasswordResetRequested(email);

        return "reset_email_sent";
    }
}
