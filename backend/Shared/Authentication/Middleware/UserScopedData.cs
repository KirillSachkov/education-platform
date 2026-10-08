using PlatformAuth.Authorization;

namespace PlatformAuth.Middleware;

/// <summary>
///     Данные текущего пользователя, извлечённые из JWT.
///     Регистрируется как Scoped-сервис; заполняется middleware-ами
///     (<see cref="UserScopedDataMiddleware" />, <see cref="DevAuthMiddleware" />)
///     в начале каждого запроса.
/// </summary>
public sealed class UserScopedData
{
    /// <summary>
    ///     UUID пользователя (sub claim).
    ///     Для сервисных client_credentials токенов — <see cref="Guid.Empty" />.
    /// </summary>
    public Guid UserId { get; set; } = Guid.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public IReadOnlyList<string> Roles { get; set; } = [];

    public IReadOnlySet<string> Permissions { get; set; } = new HashSet<string>();

    public bool IsAuthenticated { get; private set; }

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public bool HasRole(string role) =>
        Roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));

    public void Authenticate(Guid userId, string name, string email, IReadOnlyList<string> roles)
    {
        UserId = userId;
        Name = name;
        Email = email;
        Roles = roles;
        Permissions = RolePermissions.GetPermissions(roles);
        IsAuthenticated = true;
    }

    public bool IsAdmin => HasPermission(PlatformPermissions.Platform.ADMIN);

    public bool IsOwnerOrAdmin(Guid resourceAuthorId) =>
        IsAdmin || resourceAuthorId == UserId;
}
