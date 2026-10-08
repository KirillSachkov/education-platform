using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SearchService.IntegrationTests.Infrastructure;

public sealed class TestAuthHandler : SignInAuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SCHEME_NAME = "TestAuth";

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

        string schemePrefix = $"{SCHEME_NAME} ";
        if (!rawAuthorization.StartsWith(schemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string payload = rawAuthorization[schemePrefix.Length..];
        string[] parts = payload.Split('|', 4, StringSplitOptions.None);
        if (parts.Length != 4 || !Guid.TryParse(parts[0], out Guid userId))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string name = parts[1].Trim();
        string email = parts[2].Trim();
        string rolesRaw = parts[3];

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new("sub", userId.ToString()),
            new(ClaimTypes.Name, name),
            new("name", name),
            new(ClaimTypes.Email, email),
            new("email", email)
        ];

        foreach (string role in rolesRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            claims.Add(new Claim("roles", role));
        }

        ClaimsPrincipal principal = new(new ClaimsIdentity(claims, SCHEME_NAME));
        AuthenticationTicket ticket = new(principal, SCHEME_NAME);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleSignInAsync(ClaimsPrincipal user, AuthenticationProperties? properties) =>
        Task.CompletedTask;

    protected override Task HandleSignOutAsync(AuthenticationProperties? properties) =>
        Task.CompletedTask;
}
