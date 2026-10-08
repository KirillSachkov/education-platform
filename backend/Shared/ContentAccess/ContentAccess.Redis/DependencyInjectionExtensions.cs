using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ContentAccess.Redis;

public static class DependencyInjectionExtensions
{
    public static IServiceCollection AddContentAccessRedis(this IServiceCollection services, IConnectionMultiplexer redis)
    {
        services.AddSingleton(redis);
        return services.AddContentAccessRedis();
    }

    /// <summary>
    /// Registers Redis-backed content access services when the host already owns the
    /// singleton <see cref="IConnectionMultiplexer"/> registration.
    /// </summary>
    public static IServiceCollection AddContentAccessRedis(this IServiceCollection services)
    {
        services.AddSingleton<RedisEntitlementChecker>();
        services.AddSingleton<IEntitlementChecker>(sp =>
            new ResilientEntitlementChecker(
                sp.GetRequiredService<RedisEntitlementChecker>(),
                sp.GetRequiredService<ILogger<ResilientEntitlementChecker>>()));

        // IMemoryCache: нужен для короткого burst-cache user-grants (см. CachingEntitlementReader).
        // AddMemoryCache идемпотентен — если хост уже его зарегистрировал, noop.
        services.AddMemoryCache();
        services.AddSingleton<RedisEntitlementReader>();
        services.AddSingleton<IEntitlementReader>(sp =>
            new CachingEntitlementReader(
                sp.GetRequiredService<RedisEntitlementReader>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));

        services.AddSingleton<IResourceAccessWriter, RedisResourceAccessWriter>();
        services.AddSingleton<IUserGrantWriter, RedisUserGrantWriter>();
        return services;
    }
}
