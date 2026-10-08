using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using PlatformAuth.Authorization;

namespace PlatformAuth.Middleware;

/// <summary>
///     Middleware для обхода аутентификации в среде разработки (Development).
///     Если запрос не прошёл JWT-аутентификацию, автоматически заполняет
///     Scoped-сервис <see cref="UserScopedData" /> данными dev-администратора.
///
///     Поведение можно настроить через заголовки запроса:
///     <list type="bullet">
///         <item>
///             <term>X-Dev-User-Id</term>
///             <description>UUID пользователя (по умолчанию: 00000000-0000-0000-0000-000000000001)</description>
///         </item>
///         <item>
///             <term>X-Dev-Roles</term>
    ///             <description>Роли через запятую (по умолчанию: platform-admin,platform-owner)</description>
///         </item>
///     </list>
///
///     ВНИМАНИЕ: по умолчанию активен только в Development.
///     Может быть принудительно включён флагом конфигурации DevAuth:Enabled=true.
/// </summary>
public sealed class DevAuthMiddleware
{
    private const string DEV_USER_ID_HEADER = "X-Dev-User-Id";
    private const string DEV_ROLES_HEADER = "X-Dev-Roles";

    private static readonly Guid _defaultDevUserId = new("00000000-0000-0000-0000-000000000001");
    private static readonly string[] _defaultDevRoles = [PlatformRoles.ADMIN, PlatformRoles.OWNER];

    private readonly RequestDelegate _next;

    public DevAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserScopedData userScopedData)
    {
        if (!userScopedData.IsAuthenticated)
        {
            Guid userId = context.Request.Headers.TryGetValue(DEV_USER_ID_HEADER, out StringValues userIdHeader)
                          && Guid.TryParse(userIdHeader, out Guid parsedId)
                ? parsedId
                : _defaultDevUserId;

            string[] roles = context.Request.Headers.TryGetValue(DEV_ROLES_HEADER, out StringValues rolesHeader)
                ? rolesHeader.ToString()
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                : _defaultDevRoles;

            userScopedData.Authenticate(userId, "Dev Admin", "dev@local.dev", [.. roles]);

            // Устанавливаем ClaimsPrincipal, чтобы context.User.Identity.IsAuthenticated == true
            // (нужно для любого кода/middleware, который проверяет аутентификацию напрямую)
            List<Claim> claims =
            [
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Name, "Dev Admin"),
                new(ClaimTypes.Email, "dev@local.dev"),
            ];

            foreach (string role in roles)
                claims.Add(new Claim("roles", role));

            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "DevAuth"));
        }

        await _next(context);
    }
}
