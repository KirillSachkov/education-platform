using AuthService.Core.Services;
using Core.Abstractions;
using Core.Validation;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AuthService.Core.Features.Auth.UseCases;

public sealed record SendOtpRequest(string Email);

public sealed record SendOtpCommand(SendOtpRequest Request) : ICommand;

public sealed class SendOtpCommandValidator : AbstractValidator<SendOtpCommand>
{
    public SendOtpCommandValidator()
    {
        RuleFor(x => x.Request.Email)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("email"))
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"));
    }
}

public sealed class SendOtpEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/otp/send", async Task<EndpointResult<string>> (
                    [FromBody] SendOtpRequest request,
                    [FromServices] SendOtpHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new SendOtpCommand(request), ct))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("otp");
    }
}

public sealed class SendOtpHandler : ICommandHandler<string, SendOtpCommand>
{
    private readonly IAuthEmailSender _emailSender;
    private readonly IOtpStore _otpStore;
    private readonly IValidator<SendOtpCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public SendOtpHandler(
        IAuthEmailSender emailSender,
        IOtpStore otpStore,
        IValidator<SendOtpCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _emailSender = emailSender;
        _otpStore = otpStore;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        SendOtpCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        string email = command.Request.Email.Trim().ToLowerInvariant();

        string? code = await _otpStore.GenerateAndStoreAsync(email);
        if (code is null)
            return Error.Failure("auth.otp_store_failed", "Не удалось сгенерировать код подтверждения");

        bool sent = await _emailSender.SendOtpAsync(email, code, cancellationToken);
        if (!sent)
            return Error.Failure("auth.email_send_failed", "Не удалось отправить письмо с кодом подтверждения");

        _audit.LogOtpRequested(userId: null, isNewUser: null);

        return "OTP sent";
    }
}
