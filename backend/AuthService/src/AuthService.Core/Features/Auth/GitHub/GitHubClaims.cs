namespace AuthService.Core.Features.Auth.GitHub;

/// <summary>
/// Claim type constants used by GitHub OAuth responses.
/// Centralizes magic strings that appear across GitHubCallback, GitHubLink, and GitHubEmailHandler.
/// </summary>
public static class GitHubClaims
{
    public const string PREFERRED_USERNAME = "preferred_username";
    public const string URN_LOGIN = "urn:github:login";
    public const string LOGIN = "login";
    public const string EMAIL = "email";
    public const string NAME = "name";
    public const string SUB = "sub";
}
