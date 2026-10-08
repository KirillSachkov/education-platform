namespace AuthService.Core.Features.Auth.GitHub;

public static class GitHubRoutes
{
    /// <summary>
    /// Issuer returned by GitHub in OAuth authorization responses.
    /// OpenIddict 7.4 still ships the legacy https://github.com/ issuer metadata.
    /// </summary>
    public const string AUTHORIZATION_SERVER_ISSUER = "https://github.com/login/oauth";

    /// <summary>
    /// OpenIddict callback path for GitHub OAuth (used in DI config and callback endpoint).
    /// Must match the Authorization callback URL in the GitHub OAuth App.
    /// </summary>
    public const string OIDC_CALLBACK = "/auth/github/oidc-callback";

    /// <summary>
    /// Provider name used in Identity's UserLoginInfo.
    /// Must stay "GitHub" for backward compat with existing user_logins rows.
    /// </summary>
    public const string PROVIDER_NAME = "GitHub";

    public const string FLOW_KEY = "github_flow";
    public const string USER_ID_KEY = "github_link_user_id";
    public const string FLOW_LINK = "link";
    public const string FLOW_SYNC = "sync";

    /// <summary>
    /// Frontend-relative redirect target used when the disabled GitHub LOGIN flow is hit
    /// (issue #696 — RF law bans GitHub as a login method; linked accounts stay).
    /// The error code "github-login-disabled" is a frontend contract — do not change.
    /// </summary>
    public const string LOGIN_DISABLED_REDIRECT = "/login?error=github-login-disabled";
}
