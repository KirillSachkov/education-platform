using Microsoft.AspNetCore.Authorization;

namespace PlatformAuth.Authorization;

/// <summary>
///     Требование авторизации: пользователь должен иметь
///     хотя бы одну из указанных ролей.
/// </summary>
public sealed class RoleRequirement : IAuthorizationRequirement
{
    public RoleRequirement(string[] roles) => Roles = roles;
    public IReadOnlyList<string> Roles { get; }
}
