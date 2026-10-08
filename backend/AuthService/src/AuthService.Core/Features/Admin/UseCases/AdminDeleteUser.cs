using AuthService.Core.Features.Admin.Audit;
using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Admin.UseCases;

public sealed record AdminDeleteUserCommand(Guid UserId) : ICommand;

public sealed class AdminDeleteUserEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/users/{userId:guid}", async Task<EndpointResult<string>> (
                    Guid userId,
                    [FromServices] AdminDeleteUserHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new AdminDeleteUserCommand(userId), ct))
            .RequirePermissions(PlatformPermissions.Users.MANAGE)
            .WithAdminAudit(AdminAuditAction.UserDeleted);
}

public sealed class AdminDeleteUserHandler : ICommandHandler<string, AdminDeleteUserCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly UserScopedData _user;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly ILogger<AuthAudit> _audit;

    public AdminDeleteUserHandler(
        UserManager<Account> userManager,
        UserScopedData user,
        TokenRevocationService tokenRevocation,
        ILogger<AuthAudit> audit)
    {
        _userManager = userManager;
        _user = user;
        _tokenRevocation = tokenRevocation;
        _audit = audit;
    }

    public async Task<Result<string, Error>> Handle(
        AdminDeleteUserCommand command,
        CancellationToken cancellationToken)
    {
        if (command.UserId == _user.UserId)
            return AuthErrors.CannotModifySelf();

        Account? user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
            return AuthErrors.UserNotFound();

        await _tokenRevocation.RevokeAllUserTokensAsync(command.UserId, cancellationToken);

        // UserProfile cascade-deletes via FK (OnDelete.Cascade)
        IdentityResult result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
            return AuthErrors.FromIdentityError(result.Errors.First());

        _audit.LogAdminUserDeleted(_user.UserId, command.UserId);

        return "ok";
    }
}
