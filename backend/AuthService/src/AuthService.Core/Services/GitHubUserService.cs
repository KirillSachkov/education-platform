using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace AuthService.Core.Services;

public sealed class GitHubUserService : IGitHubUserService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubUserService> _logger;

    public GitHubUserService(HttpClient httpClient, ILogger<GitHubUserService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<string?, Error>> GetLoginAsync(string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return Result.Success<string?, Error>(null);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EducationPlatform", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "GitHub /user API returned {StatusCode}",
                    response.StatusCode);
                return Result.Success<string?, Error>(null);
            }

            System.Text.Json.JsonElement user = await response.Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);

            string? login = user.TryGetProperty("login", out System.Text.Json.JsonElement loginProp)
                ? loginProp.GetString()
                : null;

            return Result.Success<string?, Error>(login);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch GitHub login from /user API");
            return Result.Success<string?, Error>(null);
        }
    }
}
