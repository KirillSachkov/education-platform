using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace PlatformAuth.Authorization;

/// <summary>
///     Динамический провайдер политик авторизации.
///     Создаёт политику на лету для разрешений (префикс "Permission:")
///     и ролей (префикс "AnyRole:").
///     Это позволяет использовать .RequirePermissions() и .RequireAnyRole() на эндпоинтах
///     без ручной регистрации каждой политики.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string POLICY_PREFIX = "Permission:";
    public const string ROLE_POLICY_PREFIX = "AnyRole:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(POLICY_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            string permission = policyName[POLICY_PREFIX.Length..];

            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        if (policyName.StartsWith(ROLE_POLICY_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            string[] roles = policyName[ROLE_POLICY_PREFIX.Length..]
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            AuthorizationPolicy policy = new AuthorizationPolicyBuilder()
                .AddRequirements(new RoleRequirement(roles))
                .Build();

            return Task.FromResult<AuthorizationPolicy?>(policy);
        }

        return _fallback.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        _fallback.GetFallbackPolicyAsync();
}
