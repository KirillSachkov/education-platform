using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PlatformAuth.HttpClients;

/// <summary>
///     Extension для подключения <see cref="TokenForwardingHandler" />
///     и <see cref="ServiceTokenProvider" /> к межсервисным HttpClient-ам.
/// </summary>
public static class HttpClientAuthExtensions
{
    /// <summary>
    ///     Регистрирует <see cref="ServiceClientOptions" /> (из секции Authentication:ServiceClient),
    ///     <see cref="ServiceTokenProvider" /> (Singleton, кеш токенов),
    ///     <see cref="TokenForwardingHandler" /> (Transient) и IHttpContextAccessor.
    ///     Вызовите один раз при старте.
    ///     После этого используйте <c>AddHttpMessageHandler&lt;TokenForwardingHandler&gt;()</c>
    ///     при регистрации каждого межсервисного HttpClient.
    /// </summary>
    /// <param name="services">Коллекция сервисов.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Коллекция сервисов для chaining.</returns>
    public static IServiceCollection AddServiceTokenForwarding(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ServiceClientOptions>(
            configuration.GetSection("Authentication:ServiceClient"));

        services.AddHttpContextAccessor();
        services.AddHttpClient("ServiceTokenProvider");
        services.AddSingleton<ServiceTokenProvider>();
        services.AddTransient<TokenForwardingHandler>();

        return services;
    }
}