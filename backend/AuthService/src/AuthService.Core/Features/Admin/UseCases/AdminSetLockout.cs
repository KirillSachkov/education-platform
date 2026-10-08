using AuthService.Contracts.Admin;
using AuthService.Core.Features.Admin.Audit;
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
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminSetLockoutCommand(Guid UserId, AdminSetLockoutRequest Request) : ICommand;

public sealed class AdminSetLockoutValidator : AbstractValidator<AdminSetLockoutCommand>
{
    public AdminSetLockoutValidator()
    {
        RuleFor(x => x.Request.LockoutEnd)
            .Must(end => end > DateTimeOffset.UtcNow).WithError(GeneralErrors.ValueIsInvalid("lockoutEnd"))
            .When(x => x.Request.IsLocked && x.Request.LockoutEnd is not null);
    }
}

public sealed class AdminSetLockoutEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/{userId:guid}/lockout", async Task<EndpointResult<string>> (
                    Guid userId,
                    [FromBody] AdminSetLockoutRequest request,
                    [FromServices] AdminSetLockoutHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminSetLockoutCommand(userId, request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserLockoutSet);
}

public sealed class AdminSetLockoutHandler : ICommandHandler<string, AdminSetLockoutCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly UserScopedData _user;
    private readonly IValidator<AdminSetLockoutCommand> _validator;
    private readonly ILogger<AuthAudit> _audit;

    public AdminSetLockoutHandler(
        UserManager<Account> userManager,
        TokenRevocationService tokenRevocation,
        UserScopedData user,
        IValidator<AdminSetLockoutCommand> validator,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _tokenRevocation = tokenRevocation;
        _user = user;
        _validator = validator;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        AdminSetLockoutCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        if (command.UserId == _user.UserId)
            return AuthErrors.CannotModifySelf();

        Account? user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        // Ensure lockout is enabled for this user
        IdentityResult enabledResult = await _userManager.SetLockoutEnabledAsync(user, true);
        if (!enabledResult.Succeeded)
            return AuthErrors.FromIdentityError(enabledResult.Errors.First());

        DateTimeOffset? lockoutEnd = command.Request.IsLocked
            ? command.Request.LockoutEnd ?? DateTimeOffset.MaxValue
            : null;

        IdentityResult result = await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        if (!result.Succeeded)
            return AuthErrors.FromIdentityError(result.Errors.First());

        if (command.Request.IsLocked)
            await _tokenRevocation.RevokeAllUserTokensAsync(command.UserId, cancellationToken);

        _audit.LogAdminLockoutChanged(_user.UserId, command.UserId, command.Request.IsLocked);

        return "ok";
    }
}
