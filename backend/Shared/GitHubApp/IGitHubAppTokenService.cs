using CSharpFunctionalExtensions;
using SharedKernel;

namespace Shared.GitHubApp;

/// <summary>
///     Получает installation-токен GitHub API для конкретной installation'ы.
///     Внутри: подписывает App-level JWT (RS256, ~8 min) приватным ключом из
///     <see cref="GitHubAppOptions.PrivateKeyPemBase64"/> и обменивает его на
///     installation token через <c>POST /app/installations/{id}/access_tokens</c>.
///     Installation tokens кэшируются (TTL 50 min, GitHub выдаёт 1h).
/// </summary>
public interface IGitHubAppTokenService
{
    /// <summary>
    ///     Получить installation token для bearer-аутентификации GitHub API
    ///     запросов от имени installation'а. Cached.
    /// </summary>
    Task<Result<string, Error>> GetInstallationTokenAsync(
        long installationId, CancellationToken ct = default);

    /// <summary>
    ///     Подписать App-level JWT (RS256). Нужен для App-only endpoints
    ///     (<c>GET /app/installations/{id}</c>, <c>GET /app/installations</c>),
    ///     которые принимают только Bearer App-JWT, не installation token.
    /// </summary>
    Result<string, Error> GetAppJwt();

    /// <summary>
    ///     Сбросить кэш installation token'а. Вызвать при 401/403 от GitHub
    ///     (token revoked / installation suspended) — следующий вызов выпустит новый.
    /// </summary>
    void Invalidate(long installationId);
}
