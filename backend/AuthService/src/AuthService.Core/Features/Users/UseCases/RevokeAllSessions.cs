using AuthService.Core.Services;
using AuthService.Domain;
using Core.Abstractions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace AuthService.Core.Features.Users.UseCases;

public sealed record RevokeAllSessionsCommand : ICommand;

public sealed class RevokeAllSessionsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/users/me/sessions/revoke-all", async Task<EndpointResult<string>> (
                    [Microsoft.AspNetCore.Mvc.FromServices] RevokeAllSessionsHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new RevokeAllSessionsCommand(), ct))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class RevokeAllSessionsHandler : ICommandHandler<string, RevokeAllSessionsCommand>
{
    private readonly UserManager<Account> _userManager;
    private readonly UserScopedData _user;
    private readonly TokenRevocationService _tokenRevocation;
    private readonly ILogger<AuthAudit> _audit;

    public RevokeAllSessionsHandler(
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
        RevokeAllSessionsCommand command,
        CancellationToken cancellationToken)
    {
        Account? user = await _userManager.FindByIdAsync(_user.UserId.ToString());
        if (user is null)
            return GeneralErrors.NotFound(_user.UserId);

        await _userManager.UpdateSecurityStampAsync(user);
        await _tokenRevocation.RevokeAllUserTokensAsync(_user.UserId, cancellationToken);

        _audit.LogSessionsRevoked(_user.UserId);

        return "sessions_revoked";
    }
}
