using AuthService.Contracts.Admin;
using AuthService.Core.Features.Admin.Audit;
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
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminSetPasswordCommand(Guid UserId, AdminSetPasswordRequest Request) : ICommand;

public sealed class AdminSetPasswordValidator : AbstractValidator<AdminSetPasswordCommand>
{
    public AdminSetPasswordValidator()
    {
        RuleFor(x => x.Request.NewPassword)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("password"))
            .IsValidPassword();
    }
}

public sealed class AdminSetPasswordEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/users/{userId:guid}/password", async Task<EndpointResult<string>> (
                    Guid userId,
                    [FromBody] AdminSetPasswordRequest request,
                    [FromServices] AdminSetPasswordHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminSetPasswordCommand(userId, request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserPasswordSet);
}

public sealed class AdminSetPasswordHandler : ICommandHandler<string, AdminSetPasswordCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly SignInManager<Account> _signInManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly IValidator<AdminSetPasswordCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<AuthAudit> _audit;

    public AdminSetPasswordHandler(
        UserManager<Account> userManager,
        SignInManager<Account> signInManager,
        TokenRevocationService tokenRevocation,
        IValidator<AdminSetPasswordCommand> validator,
        UserScopedData user,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenRevocation = tokenRevocation;
        _validator = validator;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        AdminSetPasswordCommand command,
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

        if (await _userManager.HasPasswordAsync(user))
        {
            IdentityResult removeResult = await _userManager.RemovePasswordAsync(user);
            if (!removeResult.Succeeded)
                return AuthErrors.FromIdentityError(removeResult.Errors.First());
        }

        IdentityResult addResult = await _userManager.AddPasswordAsync(user, command.Request.NewPassword);
        if (!addResult.Succeeded)
            return AuthErrors.FromIdentityError(addResult.Errors.First());

        await _tokenRevocation.RevokeAllUserTokensAsync(user.Id, cancellationToken);
        await _signInManager.RefreshSignInAsync(user);

        _audit.LogPasswordChanged(user.Id);

        return "ok";
    }
}
