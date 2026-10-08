using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace PlatformAuth.Middleware;

/// <summary>
///     Middleware, который извлекает claims из JWT-токена и заполняет
///     Scoped-сервис <see cref="UserScopedData" />.
///     Должен стоять ПОСЛЕ UseAuthentication() и ДО UseAuthorization().
/// </summary>
public sealed class UserScopedDataMiddleware
{
    private const string ROLES_CLAIM_TYPE = "roles";

    private readonly RequestDelegate _next;

    public UserScopedDataMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, UserScopedData userScopedData)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            string rawUserId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                               ?? context.User.FindFirstValue("sub")
                               ?? string.Empty;

            // Для сервисных client_credentials токенов sub = client_id (не UUID) → Guid.Empty.
            _ = Guid.TryParse(rawUserId, out Guid userId);

            string name = context.User.FindFirstValue(ClaimTypes.Name)
                          ?? context.User.FindFirstValue("name")
                          ?? string.Empty;

            string email = context.User.FindFirstValue(ClaimTypes.Email)
                           ?? context.User.FindFirstValue("email")
                           ?? string.Empty;

            List<string> roles = context.User
                .FindAll(ROLES_CLAIM_TYPE)
                .Select(c => c.Value)
                .ToList();

            userScopedData.Authenticate(userId, name, email, roles);
        }

        await _next(context);
    }
}
