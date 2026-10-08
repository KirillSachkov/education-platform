using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>
/// Test auth scheme for integration tests.
/// Header format: Authorization: TestAuth {userId}|{name}|{email}|{group1,group2}
/// </summary>
public sealed class TestAuthHandler : SignInAuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "TestAuth";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string rawAuthorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(rawAuthorization))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string schemePrefix = $"{SchemeName} ";
        if (!rawAuthorization.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string payload = rawAuthorization[schemePrefix.Length..];
        if (string.IsNullOrWhiteSpace(payload))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string[] parts = payload.Split('|', 4, StringSplitOptions.None);
        if (parts.Length != 4)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string userIdRaw = parts[0].Trim();
        if (!Guid.TryParse(userIdRaw, out Guid userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string name = parts[1].Trim();
        string email = parts[2].Trim();
        string groupsRaw = parts[3];

        List<Claim> claims =
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.Name, name),
            new Claim("name", name),
            new Claim(ClaimTypes.Email, email),
            new Claim("email", email)
        ];

        string[] groups = groupsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (string group in groups)
        {
            claims.Add(new Claim("roles", group));
        }

        ClaimsIdentity identity = new(claims, SchemeName);
        ClaimsPrincipal principal = new(identity);
        AuthenticationTicket ticket = new(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>No-op sign-in — tests authenticate via header, not cookies.</summary>
    protected override Task HandleSignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties) =>
        Task.CompletedTask;

    /// <summary>No-op sign-out.</summary>
    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) =>
        Task.CompletedTask;
}
