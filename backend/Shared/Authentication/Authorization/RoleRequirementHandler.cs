using Microsoft.AspNetCore.Authorization;
using PlatformAuth.Middleware;

namespace PlatformAuth.Authorization;

/// <summary>
///     Обработчик авторизации: проверяет, имеет ли пользователь
///     хотя бы одну из требуемых ролей <see cref="RoleRequirement.Roles" />.
/// </summary>
public sealed class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly UserScopedData _userContext;

    public RoleRequirementHandler(UserScopedData userContext) => _userContext = userContext;

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        bool hasRequiredRole = requirement.Roles.Any(r => _userContext.HasRole(r));
        bool ownerCoversRequiredRole = _userContext.HasRole(PlatformRoles.OWNER)
            && requirement.Roles.Any(r => !string.Equals(r, PlatformRoles.SERVICE, StringComparison.OrdinalIgnoreCase));

        if (hasRequiredRole || ownerCoversRequiredRole)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
