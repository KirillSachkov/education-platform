namespace Shared.GitHubApp;

/// <summary>
///     Single-use, TTL-bounded store для GitHub App install-redirect state-token.
///     <typeparamref name="TData"/> — per-service контекст (например, AccessService
///     хранит <c>(AuthorId, PlanId?)</c>; ARS хранит <c>(UserId, ReturnUrl?)</c>).
///
///     Реализации: <see cref="InMemoryInstallStateStore{TData}"/> (dev / single instance)
///     и <see cref="RedisInstallStateStore{TData}"/> (multi-instance prod).
/// </summary>
public interface IInstallStateStore<TData>
    where TData : class
{
    /// <summary>Сохранить state-token с TTL. Перезапись существующего allowed.</summary>
    Task SetAsync(string stateToken, TData data, TimeSpan ttl);

    /// <summary>
    ///     Атомарно прочитать + удалить state-token (single-use). Возвращает <c>null</c>
    ///     если token отсутствует / expired.
    /// </summary>
    Task<TData?> ConsumeAsync(string stateToken);
}
