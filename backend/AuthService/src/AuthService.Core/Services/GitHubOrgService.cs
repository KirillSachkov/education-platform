using System.Net.Http.Headers;
using System.Net.Http.Json;
using AuthService.Contracts;

namespace AuthService.Core.Services;

public sealed class GitHubOrgService : IGitHubOrgService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubOrgService> _logger;

    public GitHubOrgService(
        HttpClient httpClient,
        ILogger<GitHubOrgService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Result<bool, Error>> IsMemberOfOrgAsync(
        string accessToken,
        string orgSlug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return GeneralErrors.ValueIsRequired(nameof(accessToken));
        }

        if (string.IsNullOrWhiteSpace(orgSlug))
        {
            return GeneralErrors.ValueIsRequired(nameof(orgSlug));
        }

        string requestUrl = $"user/memberships/orgs/{orgSlug}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EducationPlatform", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return Result.Success<bool, Error>(false);

        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Membership request failed. StatusCode: {StatusCode}, OrgSlug: {OrgSlug}, Body: {Body}",
                response.StatusCode,
                orgSlug,
                Truncate(body));
            return Error.Failure("auth.github.org.membership.fetch_failed", "Не удалось получить данные об организации GitHub");
        }

        GitHubOrgMembershipDto? membership = await response.Content
            .ReadFromJsonAsync<GitHubOrgMembershipDto>(cancellationToken);

        bool isActiveMember = string.Equals(membership?.State, "active", StringComparison.OrdinalIgnoreCase);
        return Result.Success<bool, Error>(isActiveMember);
    }

    public async Task<Result<IReadOnlyList<string>, Error>> FetchUserOrgsAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return GeneralErrors.ValueIsRequired(nameof(accessToken));

        const int perPage = 100;
        const int maxPages = 10; // hard cap 1000 оргов на пользователя
        var slugs = new List<string>();

        for (int page = 1; page <= maxPages; page++)
        {
            string requestUrl = $"user/orgs?per_page={perPage}&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("EducationPlatform", "1.0"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to fetch user orgs. StatusCode: {StatusCode}, Page: {Page}, Body: {Body}",
                    response.StatusCode,
                    page,
                    Truncate(body));
                return Error.Failure("auth.github.user_orgs.fetch_failed", "Не удалось получить список GitHub-организаций");
            }

            GitHubOrgDto[]? orgs = await response.Content
                .ReadFromJsonAsync<GitHubOrgDto[]>(cancellationToken);

            if (orgs is null || orgs.Length == 0)
                break;

            foreach (GitHubOrgDto org in orgs)
            {
                if (!string.IsNullOrWhiteSpace(org.Login))
                    slugs.Add(org.Login.ToLowerInvariant());
            }

            if (orgs.Length < perPage)
                break;
        }

        return Result.Success<IReadOnlyList<string>, Error>(slugs);
    }

    // Truncate response bodies in logs — GitHub responses can be large and may carry
    // SSO redirect URLs, private-repo metadata, or other sensitive header echoes. 200
    // chars is enough to identify the error class without spilling secrets to Loki.
    private static string Truncate(string body)
        => body.Length > 200 ? body[..200] + "…" : body;
}
