using Microsoft.AspNetCore.Builder;

namespace PlatformAuth.Authorization;

public static class EndpointExtensions
{
    public static TBuilder AllowAnonymousEndpoint<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.AllowAnonymous();
        return builder;
    }

    public static RouteHandlerBuilder RequirePermissions(
        this RouteHandlerBuilder builder,
        params string[] permissions)
    {
        foreach (string permission in permissions)
        {
            builder.RequireAuthorization($"{PermissionPolicyProvider.POLICY_PREFIX}{permission}");
        }

        return builder;
    }

    /// <summary>
    ///     Требует, чтобы пользователь имел хотя бы одну из указанных ролей.
    /// </summary>
    public static RouteHandlerBuilder RequireAnyRole(
        this RouteHandlerBuilder builder,
        params string[] roles)
    {
        string policyName = $"{PermissionPolicyProvider.ROLE_POLICY_PREFIX}{string.Join(',', roles)}";
        builder.RequireAuthorization(policyName);

        return builder;
    }
}
