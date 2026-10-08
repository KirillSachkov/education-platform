using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using OpenIddict.Client;

namespace AuthService.Core.Features.Auth.GitHub;

/// <summary>
/// OpenIddict client event handler that fetches the primary verified email
/// from GitHub's /user/emails API when the userinfo response has no email
/// (i.e. the user's profile email is private).
/// Runs after the built-in userinfo retrieval in the OpenIddict pipeline.
/// </summary>
public sealed class GitHubEmailHandler(
    IHttpClientFactory httpClientFactory,
    ILogger<GitHubEmailHandler> logger)
    : IOpenIddictClientHandler<OpenIddictClientEvents.ProcessAuthenticationContext>
{
    public static OpenIddictClientHandlerDescriptor Descriptor { get; }
        = OpenIddictClientHandlerDescriptor.CreateBuilder<OpenIddictClientEvents.ProcessAuthenticationContext>()
            .UseSingletonHandler<GitHubEmailHandler>()
            .SetOrder(int.MaxValue) // Run last, after all built-in handlers
            .SetType(OpenIddictClientHandlerType.Custom)
            .Build();

    public async ValueTask HandleAsync(OpenIddictClientEvents.ProcessAuthenticationContext context)
    {
        // Skip second invocation (from AuthenticateAsync in our callback endpoint)
        if (string.IsNullOrEmpty(context.BackchannelAccessToken))
            return;

        // Only handle GitHub — ProviderName/Issuer are null at this stage,
        // but the userinfo endpoint is populated from the provider's XML config
        if (context.UserInfoEndpoint?.Host is not "api.github.com")
            return;

        // Check if email is already present
        string? existingEmail = context.MergedPrincipal.FindFirst(ClaimTypes.Email)?.Value
                                ?? context.UserInfoTokenPrincipal?.FindFirst(ClaimTypes.Email)?.Value;

        if (!string.IsNullOrWhiteSpace(existingEmail))
            return;

        string? email = await FetchPrimaryEmailAsync(context.BackchannelAccessToken!);
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("GitHub /user/emails returned no verified email");
            return;
        }

        logger.LogDebug("Fetched GitHub private email via /user/emails");

        if (context.MergedPrincipal.Identity is ClaimsIdentity mergedIdentity)
            mergedIdentity.AddClaim(new Claim(ClaimTypes.Email, email));

        if (context.UserInfoTokenPrincipal?.Identity is ClaimsIdentity userInfoIdentity)
            userInfoIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
    }

    private async Task<string?> FetchPrimaryEmailAsync(string accessToken)
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SachkovTech-AuthService");

            HttpResponseMessage response = await client.GetAsync("https://api.github.com/user/emails");
            if (!response.IsSuccessStatusCode)
                return null;

            JsonElement[] emails = await response.Content
                .ReadFromJsonAsync<JsonElement[]>() ?? [];

            // Prefer primary+verified, then any verified
            foreach (JsonElement e in emails)
            {
                if (e.TryGetProperty("primary", out JsonElement primary) && primary.GetBoolean()
                    && e.TryGetProperty("verified", out JsonElement verified) && verified.GetBoolean()
                    && e.TryGetProperty("email", out JsonElement emailProp))
                    return emailProp.GetString();
            }

            foreach (JsonElement e in emails)
            {
                if (e.TryGetProperty("verified", out JsonElement verified) && verified.GetBoolean()
                    && e.TryGetProperty("email", out JsonElement emailProp))
                    return emailProp.GetString();
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch GitHub email");
            return null;
        }
    }
}
