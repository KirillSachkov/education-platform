using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using PlatformAuth.Middleware;

namespace PlatformAuth.Authorization;

/// <summary>
///     Обработчик авторизации: проверяет, есть ли у пользователя
///     требуемое разрешение в <see cref="UserScopedData.Permissions" />.
/// </summary>
public sealed class PermissionRequirementHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<PermissionRequirementHandler> _logger;

    public PermissionRequirementHandler(
        UserScopedData userScopedData,
        ILogger<PermissionRequirementHandler> logger)
    {
        _userScopedData = userScopedData;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (_userScopedData.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }
        else if (_userScopedData.IsAuthenticated)
        {
            _logger.LogWarning(
                "Permission denied: User {UserId} lacks permission {Permission}",
                _userScopedData.UserId,
                requirement.Permission);
        }

        return Task.CompletedTask;
    }
}