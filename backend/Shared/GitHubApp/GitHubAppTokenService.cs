using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SharedKernel;

namespace Shared.GitHubApp;

/// <summary>
///     Canonical implementation of <see cref="IGitHubAppTokenService"/>.
///     Extracted из <c>AccessService</c> в Shared (#296) как single-source-of-truth
///     для всех сервисов, которые ходят в GitHub App API.
///
///     <para>
///     Lifetime в DI — <c>AddSingleton</c> (или хотя бы Scoped+stable HttpClient).
///     <see cref="GitHubAppPrivateKey"/> и в нём <c>RSA</c> — non-disposable
///     (см. doc-комментарий <see cref="GitHubAppPrivateKey"/> и issue #203).
///     </para>
/// </summary>
public sealed class GitHubAppTokenService : IGitHubAppTokenService
{
    private const string GITHUB_API_BASE = "https://api.github.com";
    private const string CACHE_KEY_PREFIX = "github_app:installation_token:";
    private static readonly TimeSpan TOKEN_CACHE_TTL = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan APP_JWT_TTL = TimeSpan.FromMinutes(8); // GitHub max 10min

    private readonly GitHubAppOptions _options;
    private readonly IMemoryCache _cache;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly ILogger<GitHubAppTokenService> _logger;
    private readonly GitHubAppPrivateKey _privateKey;

    public GitHubAppTokenService(
        IOptions<GitHubAppOptions> options,
        IMemoryCache cache,
        HttpClient http,
        TimeProvider time,
        ILogger<GitHubAppTokenService> logger,
        GitHubAppPrivateKey privateKey)
    {
        _options = options.Value;
        _cache = cache;
        _http = http;
        _time = time;
        _logger = logger;
        _privateKey = privateKey;
    }

    public async Task<Result<string, Error>> GetInstallationTokenAsync(
        long installationId, CancellationToken ct = default)
    {
        if (!_options.IsConfigured)
        {
            return GitHubAppErrors.AppApiCallFailed("GitHubApp options not configured");
        }

        string cacheKey = CACHE_KEY_PREFIX
            + installationId.ToString(CultureInfo.InvariantCulture);
        if (_cache.TryGetValue(cacheKey, out string? cached) && cached is not null)
        {
            return cached;
        }

        Result<string, Error> jwtResult = TrySignAppJwt();
        if (jwtResult.IsFailure) return jwtResult.Error;

        using HttpRequestMessage req = new(
            HttpMethod.Post,
            $"{GITHUB_API_BASE}/app/installations/{installationId.ToString(CultureInfo.InvariantCulture)}/access_tokens");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtResult.Value);
        req.Headers.UserAgent.ParseAdd($"sachkov-learn/{_options.Slug}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Failed to obtain installation token (id={InstallationId}, status={Status}, body={Body})",
                    installationId, (int)resp.StatusCode, body);
                return GitHubAppErrors.AppApiCallFailed(
                    $"installation_token failed with {(int)resp.StatusCode}");
            }

            InstallationTokenResponse? body2 = await resp.Content
                .ReadFromJsonAsync<InstallationTokenResponse>(ct);
            if (body2 is null || string.IsNullOrEmpty(body2.Token))
            {
                return GitHubAppErrors.AppApiCallFailed("installation_token returned empty body");
            }

            _cache.Set(cacheKey, body2.Token, TOKEN_CACHE_TTL);
            return body2.Token;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error talking to GitHub API");
            return GitHubAppErrors.AppApiCallFailed(ex.Message);
        }
    }

    public Result<string, Error> GetAppJwt() => TrySignAppJwt();

    public void Invalidate(long installationId) =>
        _cache.Remove(CACHE_KEY_PREFIX
            + installationId.ToString(CultureInfo.InvariantCulture));

    private Result<string, Error> TrySignAppJwt()
    {
        try
        {
            DateTimeOffset now = _time.GetUtcNow();
            JwtSecurityTokenHandler handler = new();
            RsaSecurityKey key = new(_privateKey.Rsa);
            SigningCredentials creds = new(key, SecurityAlgorithms.RsaSha256);

            // GitHub requires an explicit `iat` claim — JwtSecurityToken ctor only sets `nbf` and `exp`.
            // Without it the App-level JWT is rejected with 401 "Missing 'issued at' claim".
            Claim[] claims =
            [
                new Claim(JwtRegisteredClaimNames.Iat,
                    now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                    ClaimValueTypes.Integer64),
            ];

            JwtSecurityToken jwt = new(
                issuer: _options.AppId.ToString(CultureInfo.InvariantCulture),
                audience: null,
                claims: claims,
                notBefore: now.AddSeconds(-30).UtcDateTime,
                expires: now.Add(APP_JWT_TTL).UtcDateTime,
                signingCredentials: creds);
            return handler.WriteToken(jwt);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            _logger.LogError(ex, "Failed to sign App JWT — check GitHubAppOptions.PrivateKeyPemBase64");
            return GitHubAppErrors.AppApiCallFailed("App JWT signing failed");
        }
    }

    private sealed record InstallationTokenResponse(string Token, DateTimeOffset ExpiresAt);
}
