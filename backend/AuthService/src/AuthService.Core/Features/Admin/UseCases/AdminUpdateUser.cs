using AuthService.Contracts.Admin;
using AuthService.Core.Database;
using AuthService.Core.Features.Admin.Audit;
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
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Auth.Events;

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminUpdateUserCommand(Guid UserId, AdminUpdateUserRequest Request) : ICommand;

public sealed class AdminUpdateUserValidator : AbstractValidator<AdminUpdateUserCommand>
{
    public AdminUpdateUserValidator()
    {
        RuleFor(x => x.Request.Username)
            .MinimumLength(3).WithError(GeneralErrors.ValueIsInvalid("username"))
            .MaximumLength(30).WithError(GeneralErrors.ValueIsInvalid("username"))
            .Matches(@"^[a-zA-Z0-9_\-.]+$").WithError(GeneralErrors.ValueIsInvalid("username"))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Username));

        RuleFor(x => x.Request.Email)
            .EmailAddress().WithError(GeneralErrors.ValueIsInvalid("email"))
            .When(x => !string.IsNullOrWhiteSpace(x.Request.Email));

        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Request.Username) ||
                       !string.IsNullOrWhiteSpace(x.Request.Email) ||
                       x.Request.EmailConfirmed.HasValue)
            .WithError(GeneralErrors.ValueIsInvalid("request"));
    }
}

public sealed class AdminUpdateUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/users/{userId:guid}", async Task<EndpointResult<string>> (
                    Guid userId,
                    [FromBody] AdminUpdateUserRequest request,
                    [FromServices] AdminUpdateUserHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminUpdateUserCommand(userId, request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserUpdated);
}

public sealed class AdminUpdateUserHandler : ICommandHandler<string, AdminUpdateUserCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly IOutboxService _outboxService;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<AdminUpdateUserCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<AuthAudit> _audit;

    public AdminUpdateUserHandler(
        UserManager<Account> userManager,
        IOutboxService outboxService,
        ITransactionManager transactionManager,
        IValidator<AdminUpdateUserCommand> validator,
        UserScopedData user,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _outboxService = outboxService;
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        AdminUpdateUserCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        string? currentUserName = user.UserName;

        UnitResult<Error> txResult = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (txResult.IsFailure)
            return txResult.Error;

        if (!string.IsNullOrWhiteSpace(command.Request.Username))
        {
            IdentityResult result = await _userManager.SetUserNameAsync(user, command.Request.Username.Trim());
            if (!result.Succeeded)
                return AuthErrors.FromIdentityError(result.Errors.First());
        }

        if (!string.IsNullOrWhiteSpace(command.Request.Email))
        {
            string email = command.Request.Email.Trim().ToLowerInvariant();
            IdentityResult result = await _userManager.SetEmailAsync(user, email);
            if (!result.Succeeded)
                return AuthErrors.FromIdentityError(result.Errors.First());
        }

        if (command.Request.EmailConfirmed.HasValue)
        {
            user.EmailConfirmed = command.Request.EmailConfirmed.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;

        IdentityResult updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return AuthErrors.FromIdentityError(updateResult.Errors.First());

        bool usernameChanged = !string.IsNullOrWhiteSpace(command.Request.Username) &&
                               !string.Equals(currentUserName, command.Request.Username.Trim(), StringComparison.Ordinal);

        if (usernameChanged)
            await _outboxService.PublishAsync(new UserUsernameUpdated(command.UserId, user.UserName));

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        _audit.LogAdminUserUpdated(_user.UserId, command.UserId, command.Request);

        return "ok";
    }
}
