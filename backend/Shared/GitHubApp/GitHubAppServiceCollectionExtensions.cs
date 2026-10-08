using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Shared.GitHubApp;

/// <summary>
///     DI extension'ы для подключения <see cref="GitHubAppOptions"/> + token-service
///     + state-store. Используются и AccessService, и AssignmentReviewService —
///     каждый сервис передаёт свою config-section и свой Redis keyspace.
///
///     <para>
///     Multi-app keyed DI намеренно не реализован — у каждого сервиса один GitHub App.
///     Если когда-нибудь понадобится несколько App'ов в одном процессе —
///     <c>TODO #296: keyed services for multi-app support</c>.
///     </para>
/// </summary>
public static class GitHubAppServiceCollectionExtensions
{
    /// <summary>
    ///     Биндит <see cref="GitHubAppOptions"/> на переданную config-секцию +
    ///     регистрирует singleton <see cref="GitHubAppPrivateKey"/> и
    ///     typed-HttpClient <see cref="IGitHubAppTokenService"/>.
    ///
    ///     <para>
    ///     <b>Lifetime'ы:</b>
    ///     <list type="bullet">
    ///         <item><see cref="GitHubAppPrivateKey"/> — singleton (issue #203: RSA cache
    ///         в <c>CryptoProviderFactory</c> держит ref на disposed RSA если scope'ы
    ///         пересоздают экземпляр).</item>
    ///         <item><see cref="IMemoryCache"/> — singleton (default). Хранит installation
    ///         tokens с 50min TTL.</item>
    ///         <item><see cref="IGitHubAppTokenService"/> — transient через typed
    ///         <c>HttpClient</c>. Сам сервис stateless — JWT cache и RSA живут
    ///         во внешних singleton'ах (<see cref="IMemoryCache"/> + <see cref="GitHubAppPrivateKey"/>),
    ///         так что request-scope lifetime для самой обёртки безопасен. Этот же
    ///         pattern уже работает в AccessService.</item>
    ///     </list>
    ///     </para>
    /// </summary>
    /// <param name="sectionName">
    ///     Имя config-секции. AccessService — <c>"GitHubApp"</c>;
    ///     AssignmentReviewService — <c>"AssignmentReview:GitHub"</c>.
    /// </param>
    public static IServiceCollection AddGitHubAppCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddOptions<GitHubAppOptions>()
            .Bind(configuration.GetSection(sectionName));

        services.AddMemoryCache();
        services.AddSingleton<GitHubAppPrivateKey>();

        // Typed HttpClient. AccessService использует тот же pattern (15s timeout, без Polly).
        // Polly-обёртка на этот endpoint не нужна: GitHub installation_token redeem —
        // короткий вызов, при сбое handler сам возвращает GitHubAppErrors.AppApiCallFailed
        // и call-site обычно retry'ит на более высоком уровне (UI / pipeline retry).
        services.AddHttpClient<IGitHubAppTokenService, GitHubAppTokenService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        return services;
    }

    /// <summary>
    ///     Регистрирует <see cref="InMemoryInstallStateStore{TData}"/> как реализацию
    ///     <see cref="IInstallStateStore{TData}"/>. Подходит для dev / single-instance / tests.
    /// </summary>
    /// <typeparam name="TData">
    ///     Per-service контекст state-token'а (например, <c>(AuthorId, PlanId?)</c>
    ///     в AccessService; <c>(UserId, ReturnUrl?)</c> в ARS).
    /// </typeparam>
    public static IServiceCollection AddGitHubAppInMemoryStateStore<TData>(
        this IServiceCollection services)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInstallStateStore<TData>, InMemoryInstallStateStore<TData>>();
        return services;
    }

    /// <summary>
    ///     Регистрирует <see cref="RedisInstallStateStore{TData}"/> с явно заданным
    ///     <paramref name="keyspace"/>. Требует <see cref="IConnectionMultiplexer"/>
    ///     уже зарегистрированным в DI.
    /// </summary>
    /// <param name="keyspace">
    ///     Короткий per-service namespace (например <c>"access"</c> / <c>"ars"</c>) —
    ///     попадает в Redis-ключ <c>github_app:install_state:{keyspace}:{token}</c>
    ///     чтобы два сервиса не конфликтовали.
    /// </param>
    public static IServiceCollection AddGitHubAppRedisStateStore<TData>(
        this IServiceCollection services,
        string keyspace)
        where TData : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyspace);

        services.AddSingleton<IInstallStateStore<TData>>(sp =>
            new RedisInstallStateStore<TData>(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<RedisInstallStateStore<TData>>>(),
                keyspace));

        return services;
    }
}
