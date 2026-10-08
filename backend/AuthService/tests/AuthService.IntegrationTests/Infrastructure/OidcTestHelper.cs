using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuthService.Core.Options;
using AuthService.Core.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Helpers for testing OpenIddict OIDC endpoints (authorization code, client credentials, tokens).
/// </summary>
public sealed class OidcTestHelper
{
    private readonly IntegrationTestsWebFactory _factory;
    private readonly HttpClient _cookieClient;

    public OidcTestHelper(IntegrationTestsWebFactory factory)
    {
        _factory = factory;

        // Client with cookies + NO auto-redirect (to capture authorization code from Location header)
        _cookieClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
        });
    }

    /// <summary>
    /// Seeds OpenIddict applications and scopes via PlatformConfigSyncService.
    /// Must be called before any OIDC test.
    /// </summary>
    public async Task SeedOpenIddictConfigAsync()
    {
        await using AsyncServiceScope scope = _factory.Services.CreateAsyncScope();
        PlatformConfigSyncService syncService = scope.ServiceProvider.GetRequiredService<PlatformConfigSyncService>();
        OpenIddictOptions openIddictOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<OpenIddictOptions>>().Value;
        AuthServiceOptions authOptions = scope.ServiceProvider
            .GetRequiredService<IOptions<AuthServiceOptions>>().Value;

        await syncService.SyncAsync(openIddictOptions, authOptions, CancellationToken.None);
    }

    /// <summary>
    /// Logs in via POST /auth/login and stores the Identity cookie in the shared cookie client.
    /// </summary>
    public async Task LoginAsync(string email, string password)
    {
        HttpResponseMessage response = await _cookieClient.PostAsJsonAsync(
            "/auth/login", new { email, password });

        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(
                $"Login failed: {response.StatusCode} — {await response.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Executes the full authorization code flow:
    /// 1. GET /connect/authorize (uses Identity cookie) → captures redirect with authorization code
    /// 2. POST /connect/token (exchanges code for tokens)
    /// Returns the parsed token response.
    /// </summary>
    public async Task<OidcTokenResponse> ExecuteAuthorizationCodeFlowAsync(
        string clientId, string clientSecret, string scope, string redirectUri = "http://localhost/callback")
    {
        // PKCE
        string codeVerifier = GenerateCodeVerifier();
        string codeChallenge = GenerateCodeChallenge(codeVerifier);

        // Step 1: GET /connect/authorize
        string authorizeUrl =
            $"/connect/authorize?response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&code_challenge={codeChallenge}" +
            $"&code_challenge_method=S256";

        HttpResponseMessage authorizeResponse = await _cookieClient.GetAsync(authorizeUrl);

        if (authorizeResponse.StatusCode != HttpStatusCode.Redirect &&
            authorizeResponse.StatusCode != HttpStatusCode.Found)
        {
            string body = await authorizeResponse.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Expected redirect from /connect/authorize, got {authorizeResponse.StatusCode}: {body}");
        }

        Uri location = authorizeResponse.Headers.Location!;
        string authorizationCode = ExtractQueryParam(location, "code")
            ?? throw new InvalidOperationException(
                $"No 'code' in redirect URL: {location}");

        // Step 2: POST /connect/token
        return await ExchangeCodeAsync(clientId, clientSecret, authorizationCode, redirectUri, codeVerifier);
    }

    /// <summary>
    /// Executes client credentials flow: POST /connect/token with grant_type=client_credentials.
    /// </summary>
    public async Task<OidcTokenResponse> ExecuteClientCredentialsFlowAsync(
        string clientId, string clientSecret, string scope)
    {
        using HttpClient client = _factory.CreateClient();

        FormUrlEncodedContent body = new(
        [
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
            new("scope", scope),
        ]);

        HttpResponseMessage response = await client.PostAsync("/connect/token", body);
        return await ParseTokenResponseAsync(response);
    }

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    public async Task<OidcTokenResponse> RefreshTokenAsync(
        string clientId, string clientSecret, string refreshToken)
    {
        using HttpClient client = _factory.CreateClient();

        FormUrlEncodedContent body = new(
        [
            new("grant_type", "refresh_token"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
            new("refresh_token", refreshToken),
        ]);

        HttpResponseMessage response = await client.PostAsync("/connect/token", body);
        return await ParseTokenResponseAsync(response);
    }

    /// <summary>
    /// Sends a raw client_credentials request and returns the raw HttpResponseMessage
    /// (for testing error cases).
    /// </summary>
    public async Task<HttpResponseMessage> SendClientCredentialsRawAsync(
        string clientId, string clientSecret, string scope)
    {
        using HttpClient client = _factory.CreateClient();

        FormUrlEncodedContent body = new(
        [
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
            new("scope", scope),
        ]);

        return await client.PostAsync("/connect/token", body);
    }

    /// <summary>
    /// Parses a JWT access token payload (without signature verification).
    /// </summary>
    public static JwtPayload ParseAccessToken(string accessToken)
    {
        string[] parts = accessToken.Split('.');
        if (parts.Length < 2)
            throw new InvalidOperationException("Invalid JWT format");

        string payload = parts[1];

        // Fix base64url padding
        payload = payload.Replace('-', '+').Replace('_', '/');
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }

        byte[] bytes = Convert.FromBase64String(payload);
        string json = Encoding.UTF8.GetString(bytes);

        return JsonSerializer.Deserialize<JwtPayload>(json)
            ?? throw new InvalidOperationException("Failed to parse JWT payload");
    }

    private async Task<OidcTokenResponse> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string redirectUri, string codeVerifier)
    {
        using HttpClient client = _factory.CreateClient();

        FormUrlEncodedContent body = new(
        [
            new("grant_type", "authorization_code"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
            new("code", code),
            new("redirect_uri", redirectUri),
            new("code_verifier", codeVerifier),
        ]);

        HttpResponseMessage response = await client.PostAsync("/connect/token", body);
        return await ParseTokenResponseAsync(response);
    }

    private static async Task<OidcTokenResponse> ParseTokenResponseAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new OidcTokenResponse
            {
                IsSuccess = false,
                StatusCode = response.StatusCode,
                RawResponse = json,
            };
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        return new OidcTokenResponse
        {
            IsSuccess = true,
            StatusCode = response.StatusCode,
            AccessToken = root.TryGetProperty("access_token", out JsonElement at) ? at.GetString() : null,
            RefreshToken = root.TryGetProperty("refresh_token", out JsonElement rt) ? rt.GetString() : null,
            IdToken = root.TryGetProperty("id_token", out JsonElement it) ? it.GetString() : null,
            TokenType = root.TryGetProperty("token_type", out JsonElement tt) ? tt.GetString() : null,
            ExpiresIn = root.TryGetProperty("expires_in", out JsonElement ei) ? ei.GetInt32() : 0,
            Scope = root.TryGetProperty("scope", out JsonElement sc) ? sc.GetString() : null,
            RawResponse = json,
        };
    }

    private static string? ExtractQueryParam(Uri uri, string name)
    {
        string query = uri.Query.TrimStart('?');
        foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0] == name)
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }

    private static string GenerateCodeVerifier()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed class OidcTokenResponse
{
    public bool IsSuccess { get; init; }
    public HttpStatusCode StatusCode { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public string? TokenType { get; init; }
    public int ExpiresIn { get; init; }
    public string? Scope { get; init; }
    public string? RawResponse { get; init; }
}

public sealed class JwtPayload
{
    public string? sub { get; set; }
    public string? name { get; set; }
    public string? email { get; set; }
    public string? preferred_username { get; set; }
    public string? display_name { get; set; }

    /// <summary>Audiences — can be a single string or an array in JWT.</summary>
    public JsonElement aud { get; set; }

    /// <summary>Roles — can be a single string or an array.</summary>
    public JsonElement roles { get; set; }

    /// <summary>Client ID (for client credentials tokens).</summary>
    public string? client_id { get; set; }

    /// <summary>Gets all audience values as a string collection.</summary>
    public IReadOnlyList<string> GetAudiences()
    {
        if (aud.ValueKind == JsonValueKind.String)
            return [aud.GetString()!];

        if (aud.ValueKind == JsonValueKind.Array)
            return aud.EnumerateArray().Select(e => e.GetString()!).ToArray();

        return [];
    }

    /// <summary>Gets all role values as a string collection.</summary>
    public IReadOnlyList<string> GetRoles()
    {
        if (roles.ValueKind == JsonValueKind.String)
            return [roles.GetString()!];

        if (roles.ValueKind == JsonValueKind.Array)
            return roles.EnumerateArray().Select(e => e.GetString()!).ToArray();

        return [];
    }
}
