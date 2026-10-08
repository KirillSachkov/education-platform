using Microsoft.Extensions.DependencyInjection;

namespace ContentAccess;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddContentAccess(this IServiceCollection services)
    {
        // No-op: kept for backward compatibility.
        // Actual implementations are registered by AddContentAccessRedis.
        return services;
    }
}
