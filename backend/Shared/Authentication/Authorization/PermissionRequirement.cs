using Microsoft.AspNetCore.Authorization;

namespace PlatformAuth.Authorization;

/// <summary>
///     Требование авторизации: пользователь должен иметь указанное разрешение.
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permission) => Permission = permission;
    public string Permission { get; }
}