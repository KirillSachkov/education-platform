using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AccessService.Domain;
using Shared.GitHubApp;
using GitHubAppErrors = AccessService.Domain.GitHubAppErrors;

namespace AccessService.Core.Features.Integrations.GitHubApp.Services;

public sealed class GitHubAppApiClient : IGitHubAppApiClient
{
    private const string GITHUB_API_BASE = "https://api.github.com";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly IGitHubAppTokenService _tokens;
    private readonly ILogger<GitHubAppApiClient> _logger;

    public GitHubAppApiClient(
        HttpClient http,
        IGitHubAppTokenService tokens,
        ILogger<GitHubAppApiClient> logger)
    {
        _http = http;
        _tokens = tokens;
        _logger = logger;
    }

    public async Task<Result<InstallationDetail, Error>> GetInstallationAsync(
        long installationId, CancellationToken ct)
    {
        // GET /app/installations/{id} is an App-only endpoint — accepts only Bearer App-JWT,
        // not installation tokens. Передача installation token приводит к 401 "A JSON web
        // token could not be decoded" (см. issue #202).
        Result<string, Error> jwtResult = _tokens.GetAppJwt();
        if (jwtResult.IsFailure) return jwtResult.Error;

        using HttpRequestMessage req = BuildAppJwtRequest(
            HttpMethod.Get, $"/app/installations/{installationId}", jwtResult.Value);

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "GitHub /app/installations/{Id} failed (status={Status}, body={Body})",
                    installationId, (int)resp.StatusCode, body);
                return GitHubAppErrors.AppApiCallFailed($"installations/{installationId} → {(int)resp.StatusCode}");
            }

            InstallationApiResponse? data = await resp.Content
                .ReadFromJsonAsync<InstallationApiResponse>(JsonOpts, ct);
            if (data?.Account is null)
            {
                return GitHubAppErrors.AppApiCallFailed("installation has no account");
            }

            return new InstallationDetail(data.Account.Login.ToLowerInvariant(), data.Account.Type);
        }
        catch (HttpRequestException ex)
        {
            return GitHubAppErrors.AppApiCallFailed(ex.Message);
        }
    }

    public async Task<Result<long, Error>> GetUserIdByLoginAsync(
        long installationId, string login, CancellationToken ct)
    {
        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationId, ct);
        if (tokenResult.IsFailure) return tokenResult.Error;

        using HttpRequestMessage req = BuildRequest(
            HttpMethod.Get, $"/users/{login}", tokenResult.Value);

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return GitHubAppErrors.AppApiCallFailed($"users/{login} → {(int)resp.StatusCode}");
            }

            UserApiResponse? data = await resp.Content
                .ReadFromJsonAsync<UserApiResponse>(JsonOpts, ct);
            return data?.Id ?? 0L;
        }
        catch (HttpRequestException ex)
        {
            return GitHubAppErrors.AppApiCallFailed(ex.Message);
        }
    }

    public async Task<CreateInvitationResult> CreateOrgInvitationAsync(
        long installationId, string orgLogin, long inviteeId, CancellationToken ct)
    {
        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationId, ct);
        if (tokenResult.IsFailure)
        {
            return new CreateInvitationResult.UnknownFailure(tokenResult.Error.GetMessage());
        }

        using HttpRequestMessage req = BuildRequest(
            HttpMethod.Post, $"/orgs/{orgLogin}/invitations", tokenResult.Value);
        req.Content = JsonContent.Create(new { invitee_id = inviteeId, role = "direct_member" }, options: JsonOpts);

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                CreateInvitationApiResponse? data =
                    JsonSerializer.Deserialize<CreateInvitationApiResponse>(body, JsonOpts);
                return new CreateInvitationResult.Created(data?.Id ?? 0);
            }

            if (resp.StatusCode == HttpStatusCode.UnprocessableEntity
                && body.Contains("already_a_member", StringComparison.Ordinal))
            {
                return new CreateInvitationResult.AlreadyMember();
            }

            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            {
                return new CreateInvitationResult.UserNotFound();
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _tokens.Invalidate(installationId);
                return new CreateInvitationResult.TokenInvalid();
            }

            _logger.LogWarning(
                "GitHub orgs/{Org}/invitations failed (status={Status}, body={Body})",
                orgLogin, (int)resp.StatusCode, body);
            return new CreateInvitationResult.UnknownFailure(
                $"{(int)resp.StatusCode}: {body[..Math.Min(body.Length, 200)]}");
        }
        catch (HttpRequestException ex)
        {
            return new CreateInvitationResult.UnknownFailure(ex.Message);
        }
    }

    public async Task<Result<bool, Error>> IsOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct)
    {
        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationId, ct);
        if (tokenResult.IsFailure) return tokenResult.Error;

        using HttpRequestMessage req = BuildRequest(
            HttpMethod.Get, $"/orgs/{orgLogin}/memberships/{username}", tokenResult.Value);

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return false;
            if (!resp.IsSuccessStatusCode)
            {
                return GitHubAppErrors.AppApiCallFailed($"memberships/{username} → {(int)resp.StatusCode}");
            }

            MembershipApiResponse? data = await resp.Content
                .ReadFromJsonAsync<MembershipApiResponse>(JsonOpts, ct);
            return string.Equals(data?.State, "active", StringComparison.OrdinalIgnoreCase);
        }
        catch (HttpRequestException ex)
        {
            return GitHubAppErrors.AppApiCallFailed(ex.Message);
        }
    }

    public async Task<Result<bool, Error>> RemoveOrgMemberAsync(
        long installationId, string orgLogin, string username, CancellationToken ct)
    {
        Result<string, Error> tokenResult = await _tokens.GetInstallationTokenAsync(installationId, ct);
        if (tokenResult.IsFailure) return tokenResult.Error;

        using HttpRequestMessage req = BuildRequest(
            HttpMethod.Delete, $"/orgs/{orgLogin}/memberships/{username}", tokenResult.Value);

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);

            // 204 — удалён; 404 — уже не член (или приглашение отозвано) → обе ветки success.
            if (resp.IsSuccessStatusCode || resp.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _tokens.Invalidate(installationId);
                return GitHubAppErrors.AppApiCallFailed(
                    $"memberships/{username} DELETE → {(int)resp.StatusCode} (нет права Organization members: write?)");
            }

            string body = await resp.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "GitHub DELETE orgs/{Org}/memberships/{User} failed (status={Status}, body={Body})",
                orgLogin, username, (int)resp.StatusCode, body);
            return GitHubAppErrors.AppApiCallFailed($"memberships/{username} DELETE → {(int)resp.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            return GitHubAppErrors.AppApiCallFailed(ex.Message);
        }
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string path, string installationToken)
    {
        HttpRequestMessage req = new(method, $"{GITHUB_API_BASE}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("token", installationToken);
        req.Headers.UserAgent.ParseAdd("sachkov-learn-onboarding");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return req;
    }

    private static HttpRequestMessage BuildAppJwtRequest(HttpMethod method, string path, string appJwt)
    {
        HttpRequestMessage req = new(method, $"{GITHUB_API_BASE}{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appJwt);
        req.Headers.UserAgent.ParseAdd("sachkov-learn-onboarding");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return req;
    }

    private sealed class InstallationApiResponse
    {
        [JsonPropertyName("account")] public AccountField? Account { get; set; }
    }

    private sealed class AccountField
    {
        [JsonPropertyName("login")] public string Login { get; set; } = string.Empty;
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    }

    private sealed class UserApiResponse
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }

    private sealed class CreateInvitationApiResponse
    {
        [JsonPropertyName("id")] public long Id { get; set; }
    }

    private sealed class MembershipApiResponse
    {
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
