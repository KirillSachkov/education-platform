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

public sealed record AdminSetRolesCommand(Guid UserId, AdminSetRolesRequest Request) : ICommand;

public sealed class AdminSetRolesValidator : AbstractValidator<AdminSetRolesCommand>
{
    public AdminSetRolesValidator()
    {
        RuleFor(x => x.Request.Roles)
            .NotEmpty().WithError(GeneralErrors.ValueIsInvalid("roles"));

        RuleForEach(x => x.Request.Roles)
            .Must(r => PlatformRoles.All.Contains(r))
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
    }
}

public sealed class AdminSetRolesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/users/{userId:guid}/roles", async Task<EndpointResult<string>> (
                    Guid userId,
                    [FromBody] AdminSetRolesRequest request,
                    [FromServices] AdminSetRolesHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminSetRolesCommand(userId, request), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserRolesSet);
}

public sealed class AdminSetRolesHandler : ICommandHandler<string, AdminSetRolesCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly IValidator<AdminSetRolesCommand> _validator;
    private readonly UserScopedData _user;
    private readonly ILogger<AuthAudit> _audit;

    public AdminSetRolesHandler(
        UserManager<Account> userManager,
        TokenRevocationService tokenRevocation,
        IValidator<AdminSetRolesCommand> validator,
        UserScopedData user,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _tokenRevocation = tokenRevocation;
        _validator = validator;
        _user = user;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        AdminSetRolesCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        Account? user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        IList<string> currentRoles = await _userManager.GetRolesAsync(user);
        HashSet<string> newRoles = command.Request.Roles.ToHashSet();

        // Protect: cannot remove privileged platform roles from yourself.
        if (command.UserId == _user.UserId
            && ((currentRoles.Contains(PlatformRoles.ADMIN) && !newRoles.Contains(PlatformRoles.ADMIN))
                || (currentRoles.Contains(PlatformRoles.OWNER) && !newRoles.Contains(PlatformRoles.OWNER))))
        {
            return AuthErrors.CannotModifySelf();
        }

        List<string> toRemove = currentRoles.Except(newRoles).ToList();
        List<string> toAdd = newRoles.Except(currentRoles).ToList();

        if (toRemove.Count > 0)
        {
            IdentityResult result = await _userManager.RemoveFromRolesAsync(user, toRemove);
            if (!result.Succeeded)
                return AuthErrors.FromIdentityError(result.Errors.First());
        }

        if (toAdd.Count > 0)
        {
            IdentityResult result = await _userManager.AddToRolesAsync(user, toAdd);
            if (!result.Succeeded)
                return AuthErrors.FromIdentityError(result.Errors.First());
        }

        await _tokenRevocation.RevokeAllUserTokensAsync(command.UserId, cancellationToken);

        _audit.LogAdminRolesChanged(_user.UserId, command.UserId, command.Request.Roles);

        return "ok";
    }
}
